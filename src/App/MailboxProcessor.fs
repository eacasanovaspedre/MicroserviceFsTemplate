namespace Flux.Concurrency

open Hopac
open Hopac.Infixes

[<Struct>]
type 'T AgentMailbox = private | AgentMailbox of 'T Mailbox

module AgentMailbox =

    let startAgent agent =
        let mailbox = Mailbox<_>()
        let inline takeMsg () = Mailbox.take mailbox
        {| Mailbox = AgentMailbox mailbox; Agent = takeMsg |> agent |}

    let send (AgentMailbox mailbox) msg = Mailbox.send mailbox msg

    let sendAndAwaitReply (AgentMailbox mailbox) msgBuilder =
        Alt.prepareJob
        <| fun _ ->
            let replyIVar = IVar()
            replyIVar
            |> msgBuilder
            |> Mailbox.send mailbox
            >>-. IVar.read replyIVar
