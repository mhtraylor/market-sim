module MarketSim

[<Measure>] type gold

type Amount = int * float<gold>

type Commodity =
    | Wood
    | Stone
    | Ore
    | Wheat

type Offer =
    | Ask of Commodity * int * float<gold>
    | Bid of Commodity * int * float<gold>

let getPrice = function
    | Ask (_,_,price)
    | Bid (_,_,price) -> price


let sort (bids, asks) = 
    bids |> List.sortBy (getPrice >> (*) -1.0<gold>),   // highest to lowest
    asks |> List.sortBy getPrice                        // lowest to highest

let average (bids, asks) =
    let avg = bids |> List.append asks |> List.averageBy getPrice
    (avg, bids, asks)

let calc d (Bid (x,bu,bp), Ask (y,au,ap)) =
    let noUnits = bu = 0 || au = 0
    let noPrice = bp <= 0.0<gold> || ap <= 0.0<gold>
    if noUnits || noPrice || bp - ap >= d then
        None
    else
        Some (Bid (x,bu,bp), Ask (y,au,ap))

let close (avg, bids, asks) =
    bids
    |> List.zip asks
    |> List.choose (calc avg)

type MarketCommand =
    | PostOffer of Offer
    | Close

type MarketState =
    | Open of Offer list * Offer list | Closed of (Offer * Offer) list

let market = MailboxProcessor.Start(fun inbox ->
    let rec loop state = async {
        let! msg = inbox.Receive()
        let newState =
            match msg with
            | PostOffer o ->
                match o with
                | Bid (c,m,p) as b ->
                    match state with
                    | Open (bids,asks) -> Open (b::bids,asks)
                    | c                -> c
                | Ask (c,m,p) as a ->
                    match state with
                    | Open (bids,asks) -> Open (bids,a::asks)
                    | c                -> c
            | Close       ->
                match state with
                | Open (bids,asks) -> (bids,asks) |> (sort >> average >> close) |> Closed
                | c                -> c
        do printfn "%A" newState
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

    10 |> rnd |> List.zip (rnd 10)
    |> List.iter (fun (x,y) -> Bid (Wood, x, toGold y) |> PostOffer |> market.Post)
    10 |> rnd |> List.zip (rnd 10)
    |> List.iter (fun (x,y) -> Ask (Wood, x, toGold y) |> PostOffer |> market.Post)

    market.Post(Close)
    0 // return an integer exit code
