# Updating the command-line executable

Run the published executable to check the latest stable GitHub release:

```sh
./mediana update --check
```

Install a newer version with:

```sh
./mediana update
```

On Windows, use `./mediana.exe` with the same arguments. Self-update supports
the published Windows x64, Linux x64 and macOS ARM64 executables. It preserves
the executable's location and custom filename. Linux and macOS retain the existing
file permissions. The installation directory must be writable.

The updater selects the executable for the current operating system and process
architecture. Before replacement, it verifies the download size, GitHub's SHA-256
digest, product identity and version. A check does not download the executable;
an installed newer version is never downgraded. Updating requires a published
Native AOT executable.

On Linux and macOS, the verified download replaces the installed file in the same
directory. The running process finishes with its original executable. On Windows,
a temporary PowerShell helper waits for the process to exit, verifies SHA-256
again and replaces the executable. Its result is written to `<executable>.update.log`.
Successful scheduling on Windows is reported separately from completed installation.

Ctrl+C cancels downloading and removes the staged file. Download, checksum and
version failures leave the installed executable in place. After an update, run
`./mediana --version` to confirm the installed version.
