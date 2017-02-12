﻿namespace Algorithm

open System
open System.Threading
open Nata.Core
open Nata.IO

type BinaryLogSearch(connection:Connection) =

    let indexOf, readFrom =
        connection.indexOf,
        connection.readFrom

    member x.Name = connection.name;

    member x.Execute (token:CancellationToken,
                      state:SearchState,
                      onStatus:SearchState -> unit) =
    
        let target = state.Target
        let isCancelled _ = token.IsCancellationRequested
        let isNotCancelled = isCancelled >> not
        
        let onStatus(status,position) =
            onStatus({Target=target
                      Status=status
                      Position=Some position})

        let scan (state, position) =
            connection.readFrom
            >> Seq.mapi (fun i (event, index) ->
                if i % 100 = 0 then
                    onStatus(state, { position with Current = index })
                (event, index))
            >> Seq.takeWhile isNotCancelled
            >> Seq.chooseFst Event.data
        let seek (state, position) =
            scan (state, position)
            >> Seq.tryHead

        let rec search (position:Position) =

            let range = position.UpperBound-position.LowerBound
            if range < 3000L then
                scan (Scan, position) (Position.At position.LowerBound)
                |> Seq.tryFind (fun (dt, at) -> dt >= target)
                |> function
                   | Some(dt,at) as x ->
                        onStatus(Found(dt,at),position)
                        x
                   | None as x ->
                        if isCancelled() then 
                            onStatus(Cancelled, position)
                        else
                            onStatus(NotFound, position)
                        x
            else
                let point = (range/2L)+position.LowerBound
                let position = { position with QueryAt = point }

                match seek (Seek, position) (Position.At point) with
                | None when isCancelled() ->
                    onStatus(Cancelled,position)
                    None
                | None ->
                    onStatus(NotFound,position)
                    None
                | Some(dt, at) when dt >= target ->
                    search { position with UpperBound = point }
                | Some(dt, at) ->
                    search { position with LowerBound = point }

        //// start search at the start offset
        state.Position
        |> Option.getValueOrYield(fun () ->
            let lower = indexOf Position.Start
            let upper = indexOf Position.End
            { Position.Current = lower
              Position.QueryAt = lower
              Position.LowerBound = lower
              Position.UpperBound = upper
              Position.Minimum = lower
              Position.Maximum = upper })
        |> search

    