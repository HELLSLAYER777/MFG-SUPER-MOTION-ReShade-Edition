namespace ARMO.Infrastructure

open System
open System.IO

/// Where the app keeps what is the user's: settings, what Adaptive Motion Learning has learnt, the engine's
/// log - %LOCALAPPDATA%\MFG SUPER MOTION, the app's own name. It was %LOCALAPPDATA%\ARMO: a folder of that name
/// from an earlier version is taken over whole, once, the first time this version is opened. (The engine uses
/// the same folder: sm_options.h, EngineDataPath.)
module AppData =
    let private local = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
    let folder = Path.Combine(local, "MFG SUPER MOTION")

    let private takenOver =
        lazy (
            try
                let old = Path.Combine(local, "ARMO")
                if not (Directory.Exists folder) && Directory.Exists old then Directory.Move(old, folder)
            with _ -> ())

    /// The folder (an older version's taken over first), or a place in it.
    let path (parts: string[]) =
        takenOver.Force()
        Path.Combine(Array.append [| folder |] parts)
