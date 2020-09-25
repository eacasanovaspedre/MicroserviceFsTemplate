open System
open System.Threading
open Suave
open FSharp.Control.Reactive
open Xyz
open Xyz.Shutdown
open FSharpPlus
open Hopac
open Hopac.Infixes


let private onShutdown _ = printfn "123"

[<EntryPoint>]
let main argv =
    let shutdown = Shutdown.startShutdownJob()
    use cts = new CancellationTokenSource()

    job {
        let! shutdownPromise = Promise.start shutdown.Agent
        
        let httpServer = Http.Server.startServer [Health.healthCheckEndpoint] cts.Token

        do! Job.start httpServer.Server

        let! httpServerId = 
            Shutdown.register
                (String "Suave")
                (fun () ->
                    printfn "Shuting down http server"
                    Job.thunk (fun () -> cts.Cancel())
                    >>= fun () -> httpServer.Stopped)
                []
                shutdown.Mailbox

        do! IVar.fill httpServer.OnError (fun e -> Shutdown.unregister httpServerId shutdown.Mailbox >>- konst ())

        do! Promise.read shutdownPromise
        return 0
    } |> run