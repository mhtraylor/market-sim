module MarketSim

[<Measure>] type gold

type Amount = int * float<gold>

type Commodity =
    | Wood
    | Stone
    | Ore
    | Wheat

type Details =
    { commodity : Commodity
      units     : int
      price     : float<gold>
      traderId  : int }

type Offer =
    | Ask of Details
    | Bid of Details

let getPrice = function | Ask x | Bid x -> x.price

let getUnits = function | Ask x | Bid x -> x.units

let getTraderId = function | Ask x | Bid x -> x.traderId

let toGold = float >> (*) 1.0<gold>

type Trade = Trade of int * int * Details

let trade' b a =
    let avg = (b.price + a.price) / 2.0<gold> |> toGold
    (b.traderId, a.traderId, { a with units=a.units; price=avg })
    |> Trade

let trade = function
    | Bid b, Ask a -> trade' b a
    | Ask a, Bid b -> trade' b a
    | _            -> failwith "Invalid operation"

let sort (bids, asks) = 
    bids |> List.sortBy (getPrice >> (*) -1.0<gold>),   // highest to lowest
    asks |> List.sortBy getPrice                        // lowest to highest

let average (bids, asks) =
    let avg = bids |> List.append asks |> List.averageBy (fun x -> getPrice x * (getUnits x |> float)) 
    (avg, bids, asks)

let calc avg (b,a) =
    let p = (getPrice a - getPrice b) |> abs
    if p > avg || p < avg then 
        None
    else
        Some (b,a)

let conjugate (avg, bids, asks) =
    bids
    |> List.zip asks
    |> List.choose (calc 2.0<gold>)
    |> List.map trade

let close = sort >> average >> conjugate

type MarketCommand =
    | PostOffer of Offer
    | Close of Trade list AsyncReplyChannel
    | Start

type MarketState =
    | Open of Offer list * Offer list | Closed of Trade list

let mktCloseEvent (ctx: System.Threading.SynchronizationContext) = 
    let evt = Event<Trade list>()
    evt.Publish,fun t -> ctx.Post ((fun _ -> t |> evt.Trigger), null)

let onMarketClose,sendMarketCloseEvent = 
    mktCloseEvent System.Threading.SynchronizationContext.Current

let market =
    // Setup market agent
    MailboxProcessor.Start(fun inbox ->
        let rec loop state =
            async { let! msg = inbox.Receive()
                    let newState = 
                        match state with
                        | Open (bids,asks) ->
                            match msg with
                            | PostOffer offer -> 
                                match offer with
                                | Bid _ as b -> Open (b::bids,asks)
                                | Ask _ as a -> Open (bids,a::asks)
                            | Close reply     -> 
                                let trades = (bids,asks) |> close
                                reply.Reply trades;
                                Closed trades
                            | _               ->
                                Open ([],[])
                        | _                -> state
                    return! loop newState }
        ([],[]) |> Open |> loop)

let timer round day (agent: MailboxProcessor<MarketCommand>) =
    let t = new System.Timers.Timer(round)
    t.AutoReset <- true
    async { t.Start()
            printfn "Markets open for trading"
            do! Async.Sleep day
            printfn "Markets no longer trading"
            t.Stop() }
    , t.Elapsed
    |> Observable.map (fun _ -> 
        do printfn "Closing trading round..."
        do printfn "Opening new trading round...")

// let market = MailboxProcessor.Start(fun inbox ->
//     let rec loop state = async {
//         let! msg = inbox.Receive()
//         let newState =
//             match msg with
//             | PostOffer o ->
//                 match o with
//                 | Bid x as b ->
//                     match state with
//                     | Open (bids,asks) -> Open (b::bids,asks)
//                     | c                -> c
//                 | Ask x as a ->
//                     match state with
//                     | Open (bids,asks) -> Open (bids,a::asks)
//                     | c                -> c
//             | Close r      ->
//                 match state with
//                 | Open (bids,asks) -> (bids,asks) |> close |> Closed
//                 | c                -> c
//                 |> fun (Closed trades as c) ->      // <- fix this
//                         r.Reply (trades)
//                         sendMarketCloseEvent trades
//                         c
//         return! loop newState }
//     loop (Open ([],[])))

let trader = fun _ -> 
    MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            printfn "message: %s" msg
            return! loop () }
        loop ())

[<EntryPoint>]
let main argv =
    let task,events = timer 3000.0 15000 market
    events
    |> Observable.subscribe (printfn "Trades: %A")
    |> ignore

    task |> Async.RunSynchronously

    let r = System.Random()
    let rnd cnt =
        List.init cnt (fun _ -> r.Next(0,20))
    let toGold x = float x * 1.0<gold>

    1000 |> rnd |> List.zip (rnd 1000)
    |> List.iter (fun (x,y) -> 
            Bid { commodity=Wood; units=x; price=toGold y; traderId=0 }
            |> PostOffer |> market.Post)
    1000 |> rnd |> List.zip (rnd 1000)
    |> List.iter (fun (x,y) -> 
            Ask { commodity=Wood; units=x; price=toGold y; traderId=0 } 
            |> PostOffer |> market.Post)

    let trades = market.PostAndReply(Close)
    do printfn "Trades: %A" trades
    let str = trades |> List.fold (fun s (Trade (b,a,t)) -> 
            sprintf "%s\n%d\t%f" s t.units t.price) "Units Price"
    use writer = new System.IO.StreamWriter(@"/home/mhtraylor/trades.gnuplot")
    writer.WriteLine(str)
    0 // return an integer exit code
