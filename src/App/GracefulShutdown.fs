module Xyz.Shutdown

open System
open Hopac
open Flux.Concurrency
open FSharpPlus
open Hopac.Infixes
open FSharp.Control.Reactive

type GuidOrString =
    | Guid of Guid
    | String of string

type ShutdownAgentMsg =
    | Register of GuidOrString * (unit -> Job<unit>) * GuidOrString list
    | UnRegister of GuidOrString

let private shutdownStarted = IVar()

let private startShutdown shutdownComplete data =

    let getOrCreateLatch jid latches =
        latches
        |> Map.tryFind jid
        |> Option.map (fun latch -> latches, latch)
        |> Option.defaultWith (fun () -> let latch = Latch 0 in Map.add jid latch latches, latch)

    let allConfigReady = IVar()

    let rec loop latches jobs thisConfigReady =
        function
        | (jid, (jfun, jdeps)) :: ds ->
            let latches, latch = getOrCreateLatch jid latches
            let nextConfigReady = IVar()

            let job =
                nextConfigReady
                >>= IVar.fill thisConfigReady
                >>= fun () -> allConfigReady
                >>= fun () -> latch
                >>= jfun

            let latches, job =
                jdeps
                |> List.fold (fun (latches, job) dep ->
                    let latches, latch = getOrCreateLatch dep latches
                    latches, Latch.holding latch job) (latches, job)

            loop latches (job :: jobs) nextConfigReady ds
        | [] -> jobs, thisConfigReady

    let jobs, startConfig = loop Map.empty [] allConfigReady data
    Promise.start (Job.conIgnore jobs)
    >>= (fun p -> 
            IVar.fill startConfig () 
            >>=. Promise.read p)
    >>=. IVar.fill shutdownComplete ()

type private InternalMsg =
    | Shutdown of unit IVar
    | Msg of ShutdownAgentMsg

let private shutdownAgent takeMsg =
    let shutdownAlt = shutdownStarted ^-> Shutdown
    let inline takeMsg () = takeMsg () ^-> Msg

    let rec agentLoop data =
        //printfn "AgentLoop %A" data
        shutdownAlt
        <|> takeMsg ()
        >>= function
        | Shutdown shutdownComplete -> startShutdown shutdownComplete (Map.toList data)
        | Msg msg ->
            match msg with
            | Register (jid, jfun, jdeps) -> data |> Map.add jid (jfun, jdeps) |> agentLoop
            | UnRegister j -> data |> Map.remove j |> agentLoop

    agentLoop Map.empty

let private onShutdown _ =
    let shutdownComplete = IVar()
    IVar.fill shutdownStarted shutdownComplete
    >>= (fun () -> shutdownComplete)
    |> run

let startShutdownJob () =
    AppDomain.CurrentDomain.ProcessExit.Subscribe(onShutdown)
    |> ignore
    Console.CancelKeyPress.Subscribe(onShutdown)
    |> ignore

    AgentMailbox.startAgent shutdownAgent

let register jobId jobShutdown jobDependencies mailbox =
    AgentMailbox.send mailbox (Register(jobId, jobShutdown, jobDependencies))
    >>- (konst jobId)

let unregister jobId mailbox =
    AgentMailbox.send mailbox (UnRegister jobId)
    >>- (konst jobId)
