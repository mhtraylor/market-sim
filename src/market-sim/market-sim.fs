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

let calc d (bid, ask) =
    if getPrice bid - getPrice ask >= d then None else Some (bid, ask)

let close d (avg, bids, asks) =
    bids
    |> List.zip asks
    |> List.choose (calc d)

let testBids =
    [ Bid (Wood, 1, 1.0<gold>); Bid (Wood, 2, 1.5<gold>); Bid (Wood, 3, 2.0<gold>)]
let testAsks =
    [ Ask (Wood, 1, 2.0<gold>); Ask (Wood, 2, 3.0<gold>); Ask (Wood, 3, 6.0<gold>)]

let test () =
    (testBids, testAsks)
    |> (sort >> average >> (close 2.0<gold>))
    |> printfn "%A"

// Output: [(Ask (Wood,1,2.0), Bid (Wood,3,2.0)); (Ask (Wood,2,3.0), Bid (Wood,2,1.5))]

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
                | Open (bids,asks) -> (bids,asks) |> (sort >> average >> (close 2.0<gold>)) |> Closed
                | c                -> c
        do printfn "message: %A" msg
        do printfn "state: %A" newState
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
    printfn "%A" argv
    test ()
    for a in testAsks do a |> PostOffer |> market.Post
    for b in testBids do b |> PostOffer |> market.Post
    market.Post(Close)
    0 // return an integer exit code
