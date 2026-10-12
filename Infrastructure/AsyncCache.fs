namespace ARMO.Infrastructure

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

/// Bounded LRU cache for future read-only services. Concurrent requests share a load.
/// Expiry is checked on access; there is no polling or background eviction loop.
type AsyncCache<'Key, 'Value when 'Key: equality>(capacity: int, lifetime: TimeSpan) =
    let gate = obj ()
    let order = LinkedList<'Key>()
    let entries = Dictionary<'Key, 'Value * int64 * LinkedListNode<'Key>>()
    let flights = Dictionary<'Key, TaskCompletionSource<'Value>>()
    let ttl = int64 (lifetime.TotalSeconds * float Diagnostics.Stopwatch.Frequency)

    do
        if capacity < 1 then invalidArg (nameof capacity) "Capacity must be positive."
        if lifetime <= TimeSpan.Zero then invalidArg (nameof lifetime) "Lifetime must be positive."

    let remove key =
        match entries.TryGetValue key with
        | true, (_, _, node) -> entries.Remove key |> ignore; order.Remove node
        | _ -> ()

    member _.Count = lock gate (fun () -> entries.Count)

    member _.Invalidate(key) =
        lock gate (fun () ->
            remove key
            // Detach old loads so their late result cannot repopulate the cache.
            flights.Remove key |> ignore)

    member _.Clear() = lock gate (fun () -> entries.Clear(); order.Clear(); flights.Clear())

    member _.GetAsync(key: 'Key, load: 'Key -> Task<'Value>, cancellation: CancellationToken) : Task<'Value> =
        let cached, source, ownsLoad =
            lock gate (fun () ->
                match entries.TryGetValue key with
                | true, (value, expires, node) when expires > Diagnostics.Stopwatch.GetTimestamp() ->
                    order.Remove node
                    order.AddFirst node
                    Some value, Unchecked.defaultof<_>, false
                | _ ->
                    remove key
                    match flights.TryGetValue key with
                    | true, source -> None, source, false
                    | _ ->
                        let source = TaskCompletionSource<'Value>(TaskCreationOptions.RunContinuationsAsynchronously)
                        flights.Add(key, source)
                        None, source, true)

        match cached with
        | Some value -> Task.FromResult(value).WaitAsync(cancellation)
        | None ->
            if ownsLoad then
                let work = task {
                    try
                        let! value = load key
                        lock gate (fun () ->
                            match flights.TryGetValue key with
                            | true, current when obj.ReferenceEquals(current, source) ->
                                flights.Remove key |> ignore
                                let node = order.AddFirst key
                                entries[key] <- (value, Diagnostics.Stopwatch.GetTimestamp() + ttl, node)
                                while entries.Count > capacity do remove order.Last.Value
                            | _ -> ())
                        source.TrySetResult value |> ignore
                    with error ->
                        lock gate (fun () ->
                            match flights.TryGetValue key with
                            | true, current when obj.ReferenceEquals(current, source) -> flights.Remove key |> ignore
                            | _ -> ())
                        source.TrySetException error |> ignore
                }
                work |> ignore
            // Cancelling one caller never cancels a shared read needed by other callers.
            source.Task.WaitAsync(cancellation)
