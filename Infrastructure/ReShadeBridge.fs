namespace ARMO.Infrastructure

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices
open System.Text
open System.Threading

module private BridgeNative =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type RECT =
        val mutable Left: int
        val mutable Top: int
        val mutable Right: int
        val mutable Bottom: int

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type POINT =
        val mutable X: int
        val mutable Y: int

    type EnumProc = delegate of nativeint * nativeint -> bool

    [<DllImport("user32.dll")>]
    extern int16 GetAsyncKeyState(int vKey)

    [<DllImport("user32.dll", EntryPoint = "PostMessageW")>]
    extern bool PostMessage(nativeint hWnd, uint32 msg, nativeint wParam, nativeint lParam)

    [<DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")>]
    extern uint32 MapVirtualKey(uint32 code, uint32 mapType)

    [<DllImport("user32.dll")>]
    extern bool EnumWindows(EnumProc callback, nativeint lParam)

    [<DllImport("user32.dll")>]
    extern uint32 GetWindowThreadProcessId(nativeint hWnd, uint32& processId)

    [<DllImport("user32.dll")>]
    extern bool IsWindow(nativeint hWnd)

    [<DllImport("user32.dll")>]
    extern bool IsWindowVisible(nativeint hWnd)

    [<DllImport("user32.dll")>]
    extern bool GetWindowRect(nativeint hWnd, RECT& rect)

    [<DllImport("user32.dll")>]
    extern bool GetCursorPos(POINT& point)

    [<DllImport("user32.dll")>]
    extern bool ScreenToClient(nativeint hWnd, POINT& point)

/// The ReShade overlay's own two settings, kept apart from the profiles: %LOCALAPPDATA%\MFG SUPER MOTION\reshade.cfg
/// ("enabled=1", "key=Home"). The key is the one ReShade's "Overlay key" is set to when ReShade is installed here.
module ReShadeSettings =
    let private file = AppData.path [| "reshade.cfg" |]

    let private read () =
        let values = Dictionary<string, string>()
        try
            if File.Exists file then
                for line in File.ReadAllLines file do
                    match line.IndexOf '=' with
                    | -1 -> ()
                    | at -> values[line.Substring(0, at).Trim().ToLowerInvariant()] <- line.Substring(at + 1).Trim()
        with _ -> ()
        values

    let enabled () =
        match (read ()).TryGetValue "enabled" with
        | true, value -> value = "1"
        | _ -> false

    let keyName () =
        match (read ()).TryGetValue "key" with
        | true, value when value <> "" -> value
        | _ -> "Home"

    let save (value: bool) =
        try
            Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
            File.WriteAllText(file, sprintf "enabled=%d\nkey=%s\n" (if value then 1 else 0) (keyName ()))
        with _ -> ()

    /// A key's virtual-key code and whether Windows counts it as an "extended" key.
    let keyOf (name: string) =
        match name.ToLowerInvariant() with
        | "end" -> 0x23, true
        | "insert" -> 0x2D, true
        | "delete" -> 0x2E, true
        | "pageup" -> 0x21, true
        | "pagedown" -> 0x22, true
        | "f6" -> 0x75, false
        | "f7" -> 0x76, false
        | "f8" -> 0x77, false
        | "f9" -> 0x78, false
        | "f10" -> 0x79, false
        | _ -> 0x24, true          // Home

/// ReShade lives inside the engine's process (Engine\nvngx.dll), as a renamed copy of its DLL next to it
/// (dxgi.dll covers Direct3D 10, 11 and 12). The engine folder is replaced whole by an engine update
/// (EngineStore.install), so a copy of what was installed is kept in the app's data folder and put back.
module ReShadeFiles =
    let private proxy = "dxgi.dll"
    let private stash = AppData.path [| "ReShade"; "stash" |]
    let private stashMark = Path.Combine(stash, "proxy.txt")

    let private engineRunning () =
        let count (name: string) =
            let found = Process.GetProcessesByName name
            for p in found do p.Dispose()
            found.Length
        count "nvngx" + count "nvngx.dll" > 0

    let engineFolder () =
        match EngineStore.path () with
        | Some program -> Some (Path.GetDirectoryName program)
        | None -> None

    /// A file that is a ReShade DLL (they carry the name ReShade in their exports and version resource).
    let isReShade (path: string) =
        try
            if not (File.Exists path) || FileInfo(path).Length < 512000L then false
            else
                let text = Encoding.Latin1.GetString(File.ReadAllBytes path)
                text.Contains("ReShade") || text.Contains("R\000e\000S\000h\000a\000d\000e")
        with _ -> false

    let rec copyDirectory (source: string) (target: string) =
        Directory.CreateDirectory target |> ignore
        for file in Directory.GetFiles source do
            File.Copy(file, Path.Combine(target, Path.GetFileName file), true)
        for folder in Directory.GetDirectories source do
            copyDirectory folder (Path.Combine(target, Path.GetFileName folder))

    // One value in an .ini file, in its section (made if it is not there), the rest left as it was.
    let private setIni (file: string) (section: string) (name: string) (value: string) =
        let lines = List<string>()
        if File.Exists file then lines.AddRange(File.ReadAllLines file)
        let header = "[" + section + "]"
        let start = lines.FindIndex(fun l -> String.Equals(l.Trim(), header, StringComparison.OrdinalIgnoreCase))
        if start < 0 then
            if lines.Count > 0 && lines[lines.Count - 1].Trim() <> "" then lines.Add ""
            lines.Add header
            lines.Add(name + "=" + value)
        else
            let mutable finish = lines.Count
            let mutable i = start + 1
            while i < lines.Count && finish = lines.Count do
                if lines[i].Trim().StartsWith("[") then finish <- i
                i <- i + 1
            let mutable replaced = false
            let mutable k = start + 1
            while k < finish && not replaced do
                let line = lines[k].TrimStart()
                if line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase) || line.StartsWith(name + " =", StringComparison.OrdinalIgnoreCase) then
                    lines[k] <- name + "=" + value
                    replaced <- true
                k <- k + 1
            if not replaced then lines.Insert(start + 1, name + "=" + value)
        File.WriteAllLines(file, lines)

    // ReShade_Setup_x.y.z.exe is a program with a zip appended to it: ReShade64.dll is taken out of that zip.
    let private dllFromSetup (setup: string) : byte[] option =
        let bytes = File.ReadAllBytes setup
        let mutable found : byte[] option = None
        let mutable i = 0
        while found.IsNone && i >= 0 && i < bytes.Length - 4 do
            i <- Array.IndexOf(bytes, 0x50uy, i)
            if i >= 0 then
                if i + 3 < bytes.Length && bytes[i + 1] = 0x4Buy && bytes[i + 2] = 3uy && bytes[i + 3] = 4uy then
                    try
                        use stream = new MemoryStream(bytes, i, bytes.Length - i, false)
                        use zip = new ZipArchive(stream, ZipArchiveMode.Read)
                        match zip.Entries |> Seq.tryFind (fun e -> String.Equals(e.Name, "ReShade64.dll", StringComparison.OrdinalIgnoreCase)) with
                        | Some entry ->
                            use source = entry.Open()
                            use output = new MemoryStream()
                            source.CopyTo output
                            found <- Some (output.ToArray())
                        | None -> ()
                    with _ -> ()
                i <- i + 1
        found

    let private saveStash (dir: string) =
        if Directory.Exists stash then Directory.Delete(stash, true)
        Directory.CreateDirectory stash |> ignore
        for name in [ proxy; "ReShade.ini"; "ReShadePreset.ini" ] do
            let path = Path.Combine(dir, name)
            if File.Exists path then File.Copy(path, Path.Combine(stash, name), true)
        for name in [ "reshade-shaders"; "reshade-presets" ] do
            let path = Path.Combine(dir, name)
            if Directory.Exists path then copyDirectory path (Path.Combine(stash, name))
        File.WriteAllText(stashMark, proxy)

    // Settings and presets changed since the install go into the copy too (the small files only).
    let private syncStash (dir: string) =
        for name in [ "ReShade.ini"; "ReShadePreset.ini" ] do
            let path = Path.Combine(dir, name)
            if File.Exists path then File.Copy(path, Path.Combine(stash, name), true)
        let presets = Path.Combine(dir, "reshade-presets")
        if Directory.Exists presets then copyDirectory presets (Path.Combine(stash, "reshade-presets"))

    let private finishInstall (dir: string) : Result<string, string> =
        let key = ReShadeSettings.keyName ()
        let vk, _ = ReShadeSettings.keyOf key
        setIni (Path.Combine(dir, "ReShade.ini")) "INPUT" "KeyOverlay" (sprintf "%d,0,0,0" vk)
        saveStash dir
        let shaders = Directory.Exists(Path.Combine(dir, "reshade-shaders"))
        Ok (sprintf "ReShade installed. Start motion and press %s.%s" key (if shaders then "" else " (No reshade-shaders folder yet: no effects will be listed.)"))

    /// Before the engine starts: if an engine update replaced the folder, ReShade is put back from the copy kept.
    let ensureInstalled () =
        try
            match engineFolder () with
            | Some dir when File.Exists stashMark && not (engineRunning ()) ->
                let target = Path.Combine(dir, proxy)
                if isReShade target then syncStash dir
                elif not (File.Exists target) then
                    for file in Directory.GetFiles stash do
                        if Path.GetFileName file <> "proxy.txt" then File.Copy(file, Path.Combine(dir, Path.GetFileName file), true)
                    for folder in Directory.GetDirectories stash do
                        copyDirectory folder (Path.Combine(dir, Path.GetFileName folder))
            | _ -> ()
        with _ -> ()

    let status () =
        match engineFolder () with
        | None -> "The engine is not installed yet."
        | Some dir ->
            if isReShade (Path.Combine(dir, proxy)) then
                if Directory.Exists(Path.Combine(dir, "reshade-shaders")) then "ReShade is installed."
                else "ReShade is installed, but there is no reshade-shaders folder: no effects will be listed."
            else "ReShade is not installed."

    /// `source`: a folder holding a ReShade install you already use (its DLL, ReShade.ini, reshade-shaders,
    /// reshade-presets are copied), or the path of ReShade_Setup_x.y.z.exe (only the DLL is taken from it).
    let install (source: string) : Result<string, string> =
        try
            let source = if isNull source then "" else source.Trim().Trim('"')
            match engineFolder () with
            | None -> Error "The engine is not installed yet."
            | Some dir ->
                let target = Path.Combine(dir, proxy)
                if engineRunning () then Error "Stop motion first: the engine is running."
                elif source = "" then Error "Enter a folder with a ReShade install, or the path of ReShade_Setup.exe."
                elif File.Exists target && not (isReShade target) then Error (proxy + " in the engine folder is not ReShade; it was not overwritten.")
                elif Directory.Exists source then
                    let found =
                        [ "dxgi"; "d3d11"; "d3d12"; "d3d10"; "d3d9"; "opengl32" ]
                        |> List.map (fun name -> Path.Combine(source, name + ".dll"))
                        |> List.tryFind isReShade
                    match found with
                    | None -> Error "No ReShade DLL (dxgi.dll, d3d11.dll ...) in that folder."
                    | Some dll ->
                        File.Copy(dll, target, true)
                        for name in [ "ReShade.ini"; "ReShadePreset.ini" ] do
                            let path = Path.Combine(source, name)
                            if File.Exists path then File.Copy(path, Path.Combine(dir, name), true)
                        for name in [ "reshade-shaders"; "reshade-presets" ] do
                            let path = Path.Combine(source, name)
                            if Directory.Exists path then copyDirectory path (Path.Combine(dir, name))
                        finishInstall dir
                elif File.Exists source then
                    match dllFromSetup source with
                    | None -> Error "Could not find ReShade64.dll in that file. Open the setup with 7-Zip, take ReShade64.dll out, and give the folder."
                    | Some bytes ->
                        File.WriteAllBytes(target, bytes)
                        finishInstall dir
                else Error "That path was not found."
        with error -> Error error.Message

    let uninstall () : Result<string, string> =
        try
            match engineFolder () with
            | None -> Error "The engine is not installed yet."
            | Some dir ->
                if engineRunning () then Error "Stop motion first: the engine is running."
                else
                    let target = Path.Combine(dir, proxy)
                    if isReShade target then File.Delete target
                    let log = Path.Combine(dir, "ReShade.log")
                    if File.Exists log then File.Delete log
                    if Directory.Exists stash then Directory.Delete(stash, true)
                    Ok "ReShade removed. Its settings and shaders were left in the engine folder."
        with error -> Error error.Message

/// Hears the overlay key system-wide and hands it to the engine's window, because the game has the keyboard:
/// ReShade, inside the engine, takes the keys and the mouse from the messages its window is sent. While the
/// menu is open the mouse (position and both buttons) is handed over the same way. It runs on a thread of
/// its own, looks for the engine's window twice a second, and does nothing while the engine is not running.
type ReShadeBridge() =
    let stop = new ManualResetEventSlim(false)
    let gate = obj ()
    let mutable thread : Thread = null

    let log (line: string) =
        try File.AppendAllText(AppData.path [| "reshade-bridge.log" |], DateTime.Now.ToString("HH:mm:ss") + "  " + line + "\n")
        with _ -> ()

    let enginePids () =
        [| "nvngx"; "nvngx.dll" |]
        |> Array.collect (fun name ->
            let found = Process.GetProcessesByName name
            let ids = found |> Array.map (fun p -> uint32 p.Id)
            for p in found do p.Dispose()
            ids)

    // The largest visible window of the engine, more than 300 pixels each way: the one it shows the frames in.
    let findWindow () =
        let pids = enginePids ()
        if pids.Length = 0 then 0n
        else
            let best = ref 0n
            let bestArea = ref 0L
            let callback =
                BridgeNative.EnumProc(fun hwnd _ ->
                    let mutable owner = 0u
                    BridgeNative.GetWindowThreadProcessId(hwnd, &owner) |> ignore
                    if Array.contains owner pids && BridgeNative.IsWindowVisible(hwnd) then
                        let mutable rect = BridgeNative.RECT()
                        BridgeNative.GetWindowRect(hwnd, &rect) |> ignore
                        let width = rect.Right - rect.Left
                        let height = rect.Bottom - rect.Top
                        let area = int64 width * int64 height
                        if width > 300 && height > 300 && area > bestArea.Value then
                            best.Value <- hwnd
                            bestArea.Value <- area
                    true)
            BridgeNative.EnumWindows(callback, 0n) |> ignore
            GC.KeepAlive callback
            best.Value

    let run (vk: int) (extended: bool) =
        let scan = BridgeNative.MapVirtualKey(uint32 vk, 0u)
        // WM_KEYDOWN / WM_KEYUP's lParam: repeat count, scan code, "extended key" (Home is one), then the
        // previous-state and transition bits for the key up.
        let extendedBit = if extended then 1L <<< 24 else 0L
        let down = nativeint ((1L + (int64 scan <<< 16)) + extendedBit)
        let up = nativeint ((1L + (int64 scan <<< 16)) + extendedBit + 0xC0000000L)
        let mutable target = 0n
        let mutable lastScan = 0L
        let mutable previous = false
        let mutable leftPrevious = false
        let mutable rightPrevious = false
        let mutable menuOpen = false
        let clock = Stopwatch.StartNew()
        try
            while not stop.IsSet do
                if target = 0n || not (BridgeNative.IsWindow(target)) || clock.ElapsedMilliseconds - lastScan > 500L then
                    let found = findWindow ()
                    lastScan <- clock.ElapsedMilliseconds
                    if found <> target then
                        // (A new window is a new ReShade: its menu starts closed.)
                        menuOpen <- false
                        log (sprintf "engine window -> 0x%X" (int64 found))
                    target <- found

                let pressed = BridgeNative.GetAsyncKeyState(vk) < 0s
                if pressed && not previous && target <> 0n then
                    BridgeNative.PostMessage(target, 0x100u, nativeint vk, down) |> ignore
                    Thread.Sleep 60
                    BridgeNative.PostMessage(target, 0x101u, nativeint vk, up) |> ignore
                    menuOpen <- not menuOpen
                    log (sprintf "key sent, menu %s" (if menuOpen then "open" else "closed"))
                previous <- pressed

                if menuOpen && target <> 0n then
                    let mutable point = BridgeNative.POINT()
                    if BridgeNative.GetCursorPos(&point) then
                        BridgeNative.ScreenToClient(target, &point) |> ignore
                        let position = nativeint (int64 ((point.X &&& 0xFFFF) ||| ((point.Y &&& 0xFFFF) <<< 16)))
                        BridgeNative.PostMessage(target, 0x200u, 0n, position) |> ignore
                        let left = BridgeNative.GetAsyncKeyState(0x01) < 0s
                        if left && not leftPrevious then BridgeNative.PostMessage(target, 0x201u, 1n, position) |> ignore
                        elif not left && leftPrevious then BridgeNative.PostMessage(target, 0x202u, 0n, position) |> ignore
                        leftPrevious <- left
                        let right = BridgeNative.GetAsyncKeyState(0x02) < 0s
                        if right && not rightPrevious then BridgeNative.PostMessage(target, 0x204u, 2n, position) |> ignore
                        elif not right && rightPrevious then BridgeNative.PostMessage(target, 0x205u, 0n, position) |> ignore
                        rightPrevious <- right
                    Thread.Sleep 8
                elif target = 0n then Thread.Sleep 250
                else Thread.Sleep 30
        with error -> log ("stopped: " + error.Message)

    /// Starts listening (nothing happens until the engine has a window).
    member _.Start() =
        lock gate (fun () ->
            if isNull thread then
                stop.Reset()
                let vk, extended = ReShadeSettings.keyOf (ReShadeSettings.keyName ())
                let created = Thread(ThreadStart(fun () -> run vk extended), IsBackground = true, Name = "reshade-bridge")
                thread <- created
                created.Start())

    member _.Stop() =
        lock gate (fun () ->
            if not (isNull thread) then
                stop.Set()
                thread.Join(1000) |> ignore
                thread <- null)

    interface IDisposable with
        member this.Dispose() = this.Stop()
