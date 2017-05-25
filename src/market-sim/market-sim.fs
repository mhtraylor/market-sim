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

let getUnits = function
    | Ask (_,units,_)
    | Bid (_,units,_) -> units

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
                | Open (bids,asks) -> (bids,asks) |> close |> Closed
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

    100 |> rnd |> List.zip (rnd 100)
    |> List.iter (fun (x,y) -> Bid (Wood, x, toGold y) |> PostOffer |> market.Post)
    100 |> rnd |> List.zip (rnd 100)
    |> List.iter (fun (x,y) -> Ask (Wood, x, toGold y) |> PostOffer |> market.Post)

    market.Post(Close)
    System.Threading.Thread.Sleep(10000)
    0 // return an integer exit code
