module MarketSim

[<Measure>] type gold

type Amount = int * float<gold>

type Commodity =
    | Wood
    | Stone
    | Ore
    | Wheat

type Offer =
    | Ask of Details
    | Bid of Details
and
    Details =
    { commodity : Commodity
      units     : int
      price     : float<gold>
      traderId  : int }

let getPrice = function | Ask x | Bid x -> x.price

let getUnits = function | Ask x | Bid x -> x.units

let getTraderId = function | Ask x | Bid x -> x.traderId

let toGold = float >> (*) 1.0<gold>

let sort (bids, asks) = 
    bids |> List.sortBy (getPrice >> (*) -1.0<gold>),   // highest to lowest
    asks |> List.sortBy getPrice                        // lowest to highest

let average (bids, asks) =
    let avg = bids |> List.append asks |> List.averageBy (fun x -> getPrice x * (getUnits x |> toGold)) 
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

let close = sort >> average >> conjugate

type MarketCommand =
    | PostOffer of Offer
    | Close of (Offer * Offer) list AsyncReplyChannel

type MarketState =
    | Open of Offer list * Offer list | Closed of (Offer * Offer) list

let mktCloseEvent (ctx: System.Threading.SynchronizationContext) = 
    let evt = Event<(Offer * Offer) list>()
    evt.Publish,fun t -> ctx.Post ((fun _ -> t |> evt.Trigger), null)

let onMarketClose,sendMarketCloseEvent = 
    mktCloseEvent System.Threading.SynchronizationContext.Current

let market = MailboxProcessor.Start(fun inbox ->
    let rec loop state = async {
        let! msg = inbox.Receive()
        let newState =
            match msg with
            | PostOffer o ->
                match o with
                | Bid x as b ->
                    match state with
                    | Open (bids,asks) -> Open (b::bids,asks)
                    | c                -> c
                | Ask x as a ->
                    match state with
                    | Open (bids,asks) -> Open (bids,a::asks)
                    | c                -> c
            | Close r      ->
                match state with
                | Open (bids,asks) -> (bids,asks) |> close |> Closed
                | c                -> c
                |> fun (Closed trades as c) ->      // <- fix this
                        r.Reply (trades)
                        sendMarketCloseEvent trades
                        c
        return! loop newState }
    loop (Open ([],[])))

let trader = fun _ -> 
    MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            printfn "message: %s" msg
            return! loop () }
        loop ())

[<EntryPoint>]
let main argv =
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
    let str = trades |> List.fold (fun s (a,b) -> 
            sprintf "%s\n%d\t%f\t%d\t%f" s (getUnits a) (getPrice a) (getUnits b) (getPrice b)) "AskUnits AskPrice BidUnits BidPrice"
    use writer = new System.IO.StreamWriter(@"/home/mtraylor/trades.gnuplot")
    writer.WriteLine(str)
    0 // return an integer exit code
