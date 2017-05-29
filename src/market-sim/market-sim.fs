module MarketSim

// Units

[<Measure>] type kg
[<Measure>] type gold

// Types

type Amount = int<kg> * float<gold>

type Commodity =
    | Wood
    | Stone
    | Ore
    | Wheat

type Offer =
    { commodity : Commodity
      units     : int<kg>
      price     : float<gold> } // not a tuple as more items may be added

type Trade = Trade of Offer * Offer * Offer

// Operators

let price x = x.price
let isZeroUnits x = x.units <= 0<kg>
let isZeroPrice x = x.price <= 0.<gold>
let (!^) x = x.units <= 0<kg>
let (!%) x = x.price <= 0.<gold>
let (!@) x = ((!^) x || (!%) x) |> not

// Combinators

let sort (bids,asks) =
    bids |> List.sortByDescending price 
    |> List.filter (!@),
    asks |> List.sortBy price
    |> List.filter (!@)

let resolve (bids,asks) =
    let rec loop bids asks acc =
        match bids,asks with
        | ([],_) | (_,[]) -> acc
        | (hb::tb,ha::ta) -> 
            let p,u = (ha.price + hb.price) / 2., min ha.units hb.units
            if u <= 0<kg> then
                loop tb asks acc
            else
                let t = { ha with price=p; units=u }
                loop tb ta (Trade (t,ha,hb) :: acc)
    loop bids asks []

let close = sort >> resolve

// Agents

open System.Threading

type MarketCommand =
    | PostBid of Offer
    | PostAsk of Offer
    | Close
    | CloseAndReply of Trade list AsyncReplyChannel
    | Start

type MarketState =
    | Open of Offer list * Offer list | Closed of Trade list

let makeMarketEvent (ctx: SynchronizationContext) = 
    let evt = Event<Trade list>()
    evt.Publish,fun t -> evt.Trigger t
    // evt.Publish,fun t -> ctx.Post ((fun _ -> t |> evt.Trigger), null)

let market ctx =
    let evt,fire = makeMarketEvent ctx
    // Setup market agent
    let agent = MailboxProcessor.Start(fun inbox ->
        let rec loop state =
            async { let! msg = inbox.Receive()
                    let newState = 
                        match state with
                        | Open (bids,asks) ->
                            match msg with
                            | PostBid b   -> Open (b::bids,asks)
                            | PostAsk a   -> Open (bids,a::asks)
                            | Close       ->
                                printfn "Closing trades"
                                let trades = (bids,asks) |> close
                                fire trades
                                printfn "Traders notified"
                                Closed trades
                            | CloseAndReply r ->
                                match state with
                                | Closed t -> r.Reply t; Closed t
                                | s -> s
                            | _               ->
                                Open ([],[])
                        | _                -> state
                    return! loop newState }
        ([],[]) |> Open |> loop)
    agent,evt

let timer round day =
    let t = new System.Timers.Timer(round)
    t.AutoReset <- true
    async { t.Start()
            printfn "Markets open for trading"
            do! Async.Sleep day
            printfn "Markets no longer trading"
            t.Stop() },
    t.Elapsed |> Observable.map (fun _ -> Close)

let trader = fun _ -> 
    MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            printfn "message: %s" msg
            return! loop () }
        loop ())

[<EntryPoint>]
let main argv =
    // Simple random demo
    let market,onMarketClose = market SynchronizationContext.Current
    let task,event = timer 3000.0 15000

    let r = System.Random()
    let rnd cnt =
        List.init cnt (fun _ -> r.Next(0,20))
    let toGold x = float x * 1.0<gold>

    10000 |> rnd |> List.zip (rnd 10000)
    |> List.iter (fun (x,y) -> 
            { commodity=Wood; units=x * 1<kg>; price=toGold y }
            |> PostAsk |> market.Post)
    10000 |> rnd |> List.zip (rnd 10000)
    |> List.iter (fun (x,y) -> 
            { commodity=Wood; units=x * 1<kg>; price=toGold y } 
            |> PostBid |> market.Post)

    onMarketClose 
        |> Observable.add (printfn "Trades: %A")

    event 
        |> Observable.add (market.Post)

    task |> Async.RunSynchronously

    // let trades = market.PostAndReply(CloseAndReply)
    // do printfn "Trades: %A" trades
    // let str = trades |> List.sortBy (fun (Trade(s,_,_)) -> s.units) |> List.fold (fun s (Trade (b,a,t)) -> 
    //         let p = (float(b.units) * b.price)
    //         let u = (b.price / float(b.units))
    //         sprintf "%s\n%d\t%f\t%f\t%f" s b.units b.price p u) ""
    // use writer = new System.IO.StreamWriter(@"/home/mhtraylor/trades.gnuplot")
    // writer.WriteLine(str)
    0 // return an integer exit code

// let mktCloseEvent (ctx: System.Threading.SynchronizationContext) = 
//     let evt = Event<Trade list>()
//     evt.Publish,fun t -> ctx.Post ((fun _ -> t |> evt.Trigger), null)

// let onMarketClose,sendMarketCloseEvent = 
//     mktCloseEvent System.Threading.SynchronizationContext.Current
