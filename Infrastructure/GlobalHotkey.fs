namespace ARMO.Infrastructure

open System
open System.Runtime.InteropServices
open System.Threading

module private HotkeyNative =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type MSG =
        val mutable hwnd: nativeint
        val mutable message: uint32
        val mutable wParam: nativeint
        val mutable lParam: nativeint
        val mutable time: uint32
        val mutable x: int
        val mutable y: int

    [<DllImport("user32.dll", SetLastError = true)>]
    extern bool RegisterHotKey(nativeint hWnd, int id, uint32 fsModifiers, uint32 vk)

    [<DllImport("user32.dll")>]
    extern bool UnregisterHotKey(nativeint hWnd, int id)

    [<DllImport("user32.dll", EntryPoint = "GetMessageW")>]
    extern int GetMessage(MSG& lpMsg, nativeint hWnd, uint32 wMsgFilterMin, uint32 wMsgFilterMax)

    [<DllImport("user32.dll", EntryPoint = "PeekMessageW")>]
    extern bool PeekMessage(MSG& lpMsg, nativeint hWnd, uint32 wMsgFilterMin, uint32 wMsgFilterMax, uint32 wRemoveMsg)

    [<DllImport("user32.dll", EntryPoint = "PostThreadMessageW")>]
    extern bool PostThreadMessage(uint32 idThread, uint32 Msg, nativeint wParam, nativeint lParam)

    [<DllImport("kernel32.dll")>]
    extern uint32 GetCurrentThreadId()

    let WM_HOTKEY = 0x0312u
    let WM_QUIT = 0x0012u
    let WM_APP_SET = 0x8001u
    let MOD_NOREPEAT = 0x4000u

/// One system-wide shortcut (START / STOP MOTION), heard while a game has the
/// keyboard and while the window is in the tray. It lives on a thread of its
/// own that only waits for Windows' message - nothing is polled.
type GlobalHotkey() =
    let pressed = Event<unit>()
    let ready = new ManualResetEventSlim(false)
    let applied = new AutoResetEvent(false)
    let mutable threadId = 0u
    let mutable wanted = 0          // modifiers * 65536 + virtual key; 0 none
    let mutable registered = false
    let mutable taken = false       // the shortcut wanted belongs to another program
    let mutable started = false

    let apply () =
        if registered then
            HotkeyNative.UnregisterHotKey(0n, 1) |> ignore
            registered <- false
        let value = Volatile.Read(&wanted)
        if value > 0 then
            let modifiers = (uint32 (value >>> 16) &&& 0xFu) ||| HotkeyNative.MOD_NOREPEAT
            registered <- HotkeyNative.RegisterHotKey(0n, 1, modifiers, uint32 (value &&& 0xFFFF))
            taken <- not registered
        else taken <- false
        applied.Set() |> ignore

    let run () =
        let mutable message = HotkeyNative.MSG()
        // The first call makes this thread's message queue.
        HotkeyNative.PeekMessage(&message, 0n, 0u, 0u, 0u) |> ignore
        threadId <- HotkeyNative.GetCurrentThreadId()
        apply ()
        ready.Set()
        let mutable alive = true
        while alive do
            let result = HotkeyNative.GetMessage(&message, 0n, 0u, 0u)
            if result <= 0 then alive <- false
            elif message.message = HotkeyNative.WM_HOTKEY then pressed.Trigger()
            elif message.message = HotkeyNative.WM_APP_SET then apply ()
        if registered then HotkeyNative.UnregisterHotKey(0n, 1) |> ignore

    let thread = Thread(run, IsBackground = true, Name = "hotkey")

    member _.Pressed = pressed.Publish

    /// True when Windows refused the shortcut last asked for (another program holds it).
    member _.IsTaken = taken

    /// modifiers * 65536 + virtual key (RegisterHotKey's values); 0 for none.
    member _.Set(value: int) =
        Volatile.Write(&wanted, value)
        if not started then
            started <- true
            thread.Start()
            ready.Wait 2000 |> ignore
        else
            applied.Reset() |> ignore
            if HotkeyNative.PostThreadMessage(threadId, HotkeyNative.WM_APP_SET, 0n, 0n) then
                applied.WaitOne 500 |> ignore

    interface IDisposable with
        member _.Dispose() =
            if threadId <> 0u then HotkeyNative.PostThreadMessage(threadId, HotkeyNative.WM_QUIT, 0n, 0n) |> ignore
