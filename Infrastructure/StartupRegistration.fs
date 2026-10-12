namespace ARMO.Infrastructure

open System
open System.Security
open Microsoft.Win32

type IStartupRegistration =
    abstract IsEnabled: bool
    abstract SetEnabled: bool -> Result<unit, string>

module StartupRegistration =
    let command (executable: string) = sprintf "\"%s\" --startup" executable

    let forCurrentProcess () =
        let path = "Software\\Microsoft\\Windows\\CurrentVersion\\Run"
        let name = "MFG SUPER MOTION"
        let legacyNames = [| "DLSS MFG For Everyone"; "DLSS SUPER MOTION"; "Octo Super Motion" |]
        let executable = Environment.ProcessPath
        let supported = OperatingSystem.IsWindows() &&
                        not (String.IsNullOrWhiteSpace executable) &&
                        ([| "Mfg Super Motion"; "MfgSuperMotion" |]
                         |> Array.exists (fun name -> String.Equals(System.IO.Path.GetFileNameWithoutExtension executable, name, StringComparison.OrdinalIgnoreCase)))
        // Preserve an already enabled startup entry across the product/executable rename.
        // An unregistered installation stays off; opening it creates no registry entry.
        if supported then
            try
                use existing = Registry.CurrentUser.OpenSubKey(path, false)
                let legacyEnabled = not (isNull existing) && legacyNames |> Array.exists (fun legacy -> not (isNull (existing.GetValue(legacy))))
                if legacyEnabled then
                    use writable = Registry.CurrentUser.OpenSubKey(path, true)
                    writable.SetValue(name, command executable, RegistryValueKind.String)
                    legacyNames |> Array.iter (fun legacy -> writable.DeleteValue(legacy, false))
            with :? SecurityException | :? UnauthorizedAccessException | :? System.IO.IOException -> ()
        { new IStartupRegistration with
            member _.IsEnabled =
                if not supported then false
                else
                    try
                        use key = Registry.CurrentUser.OpenSubKey(path, false)
                        not (isNull key) && string (key.GetValue(name, "")) = command executable
                    with :? SecurityException | :? UnauthorizedAccessException | :? System.IO.IOException -> false

            member _.SetEnabled enabled =
                if not supported then Error "Windows startup requires the application executable."
                elif enabled && (command executable).Length > 260 then Error "The executable path is too long for Windows startup."
                else
                    try
                        use key = Registry.CurrentUser.CreateSubKey(path, true)
                        if enabled then key.SetValue(name, command executable, RegistryValueKind.String)
                        else key.DeleteValue(name, false)
                        // Explicit changes also remove the previous product's entry,
                        // so a rename cannot leave two copies starting with Windows.
                        legacyNames |> Array.iter (fun legacy -> key.DeleteValue(legacy, false))
                        Ok ()
                    with
                    | :? SecurityException as error -> Error error.Message
                    | :? UnauthorizedAccessException as error -> Error error.Message
                    | :? System.IO.IOException as error -> Error error.Message }
