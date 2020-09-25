module Xyz.Http.Server

open Suave
open Hopac
open Hopac.Infixes
open System.Threading

let startServer endpoints cancellationToken onError =

    let config =
        { defaultConfig with
              cancellationToken = cancellationToken }

    let endpoints = choose endpoints

    let startData, server = startWebServerAsync config endpoints
    let stopped = IVar()
    let serverJob =
        Job.onThreadPool (fun () -> Async.RunSynchronously(server, cancellationToken = cancellationToken))

    {| StartData = startData |> Job.fromAsync
       Server =
           Job.tryWith serverJob (function
               | :? System.OperationCanceledException -> Job.result ()
               | e -> onError e)
           >>= (fun () -> IVar.fill stopped ())
       Stopped = stopped |}
