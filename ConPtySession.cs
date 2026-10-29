using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace test;

public sealed class ConPtySession : IDisposable
{
    private IntPtr pseudoConsole = IntPtr.Zero;
    private IntPtr inputWrite = IntPtr.Zero;
    private IntPtr outputRead = IntPtr.Zero;
    private IntPtr processHandle = IntPtr.Zero;
    private IntPtr threadHandle = IntPtr.Zero;
    private IntPtr attributeList = IntPtr.Zero;

    private volatile bool disposed;
    private bool readingStarted;

    public event Action<string>? OutputReceived;

    public ConPtySession(
        string shell,
        string shellArguments,
        string? shellPath = null,
        short columns = 100,
        short rows = 30,
        string? workingDirectory = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "ConPTY works only on Windows.");

        Start(shell, shellArguments, shellPath, columns, rows, workingDirectory);
    }

    private void Start(
        string shell,
        string shellArguments,
        string? shellPath,
        short columns,
        short rows,
        string? workingDirectory)
    {
        IntPtr inputRead = IntPtr.Zero;
        IntPtr outputWrite = IntPtr.Zero;

        try
        {
            var security = new SECURITY_ATTRIBUTES
            {
                Length = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                InheritHandle = false
            };

            if (!CreatePipe(out inputRead, out inputWrite, ref security, 0))
                throw new IOException(
                    "Could not create input pipe. Error: " +
                    Marshal.GetLastWin32Error());

            if (!CreatePipe(out outputRead, out outputWrite, ref security, 0))
                throw new IOException(
                    "Could not create output pipe. Error: " +
                    Marshal.GetLastWin32Error());

            var size = new COORD
            {
                X = Math.Max((short)1, columns),
                Y = Math.Max((short)1, rows)
            };

            int result = CreatePseudoConsole(
                size,
                inputRead,
                outputWrite,
                0,
                out pseudoConsole
            );

            if (result < 0)
                Marshal.ThrowExceptionForHR(result);

            CloseHandle(inputRead);
            inputRead = IntPtr.Zero;

            CloseHandle(outputWrite);
            outputWrite = IntPtr.Zero;

            IntPtr attributeSize = IntPtr.Zero;

            InitializeProcThreadAttributeList(
                IntPtr.Zero, 1, 0, ref attributeSize);

            attributeList = Marshal.AllocHGlobal(attributeSize);

            if (!InitializeProcThreadAttributeList(
                attributeList, 1, 0, ref attributeSize))
            {
                throw new IOException(
                    "Could not initialize process attributes. Error: " +
                    Marshal.GetLastWin32Error());
            }

            if (!UpdateProcThreadAttribute(
                attributeList,
                0,
                (IntPtr)0x00020016, // PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE
                pseudoConsole,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new IOException(
                    "Could not configure ConPTY. Error: " +
                    Marshal.GetLastWin32Error());
            }

            var startup = new STARTUPINFOEX
            {
                StartupInfo = new STARTUPINFO(),
                AttributeList = attributeList
            };

            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

            string shellName = shell.Trim();
            if (shellName.Length == 0)
                throw new ArgumentException(
                    "The configured shell executable cannot be empty.",
                    nameof(shell));

            string configuredPath = shellPath?.Trim() ?? "";
            string executable = configuredPath.Length > 0
                ? configuredPath
                : shellName;

            if (configuredPath.Length > 0 && !File.Exists(configuredPath))
            {
                throw new FileNotFoundException(
                    $"Configured shell-path does not exist: '{configuredPath}'.",
                    configuredPath);
            }

            string commandLine = QuoteExecutablePath(executable);
            if (!string.IsNullOrWhiteSpace(shellArguments))
                commandLine += " " + shellArguments.Trim();

            var command = new StringBuilder(commandLine);
            string? applicationName = configuredPath.Length > 0 ||
                                      Path.IsPathRooted(executable) ||
                                      executable.Contains(Path.DirectorySeparatorChar) ||
                                      executable.Contains(Path.AltDirectorySeparatorChar)
                ? executable
                : null;

            string startupDirectory = !string.IsNullOrWhiteSpace(workingDirectory) &&
                                      Directory.Exists(workingDirectory)
                ? workingDirectory
                : Environment.CurrentDirectory;

            string shellNameForEnvironment = Path.GetFileName(executable)
                .ToLowerInvariant();
            bool isCmd = shellNameForEnvironment is "cmd" or "cmd.exe";
            IntPtr environmentBlock = CreateEnvironmentBlock(isCmd);

            bool processCreated;
            try
            {
                processCreated = CreateProcess(
                applicationName,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                environmentBlock,
                startupDirectory,
                ref startup,
                out PROCESS_INFORMATION processInfo);

                if (processCreated)
                {
                    processHandle = processInfo.Process;
                    threadHandle = processInfo.Thread;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            if (!processCreated)
            {
                throw new IOException(
                    $"Could not start configured shell '{executable}'. " +
                    "Set shell-path to the full path of the shell executable. Error: " +
                    Marshal.GetLastWin32Error());
            }
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (inputRead != IntPtr.Zero)
                CloseHandle(inputRead);

            if (outputWrite != IntPtr.Zero)
                CloseHandle(outputWrite);
        }
    }

    private static string QuoteExecutablePath(string path)
    {
        string unquotedPath = path.Length >= 2 &&
                              path[0] == '"' &&
                              path[^1] == '"'
            ? path[1..^1]
            : path;

        return $"\"{unquotedPath.Replace("\"", "\\\"")}\"";
    }

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    private static IntPtr CreateEnvironmentBlock(bool configureCmdPrompt)
    {
        string block = BuildEnvironmentBlock(configureCmdPrompt);
        byte[] bytes = Encoding.Unicode.GetBytes(block);
        IntPtr memory = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);
        return memory;
    }

    internal static string BuildEnvironmentBlock(bool configureCmdPrompt)
    {
        var variables = new SortedDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (variable.Key is string key && variable.Value is string value)
                variables[key] = value;
        }

        if (configureCmdPrompt)
            variables["PROMPT"] = "$E]9;9;$P$E\\$P$G";

        return string.Join(
            '\0',
            variables.Select(variable => $"{variable.Key}={variable.Value}")) +
            "\0\0";
    }

    // Kliči po priklopu dogodka OutputReceived.
    public void StartReading()
    {
        if (readingStarted || disposed)
            return;

        readingStarted = true;
        _ = Task.Run(ReadOutput);
    }

    private void ReadOutput()
    {
        byte[] buffer = new byte[4096];
        char[] chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        Decoder decoder = Encoding.UTF8.GetDecoder();

        while (!disposed && outputRead != IntPtr.Zero)
        {
            bool success;

            try
            {
                success = ReadFile(
                    outputRead,
                    buffer,
                    (uint)buffer.Length,
                    out uint bytesRead,
                    IntPtr.Zero
                );

                if (!success || bytesRead == 0)
                    break;

                int charCount = decoder.GetChars(
                    buffer,
                    0,
                    (int)bytesRead,
                    chars,
                    0,
                    false
                );

                if (charCount > 0)
                {
                    string output = new string(chars, 0, charCount);
                    OutputReceived?.Invoke(output);
                }
            }
            catch (Exception ex)
            {
                if (!disposed)
                    DuckyLog.Log("Terminal output error: " + ex.Message);
                break;
            }
        }
    }

    public void WriteInput(string input)
    {
        if (disposed || inputWrite == IntPtr.Zero)
            return;

        byte[] bytes = Encoding.UTF8.GetBytes(input);
        int offset = 0;

        while (offset < bytes.Length && !disposed)
        {
            byte[] remaining = new byte[bytes.Length - offset];
            Array.Copy(bytes, offset, remaining, 0, remaining.Length);

            if (!WriteFile(
                inputWrite,
                remaining,
                (uint)remaining.Length,
                out uint bytesWritten,
                IntPtr.Zero))
            {
                throw new IOException(
                    "Could not write to ConPTY. Error: " +
                    Marshal.GetLastWin32Error());
            }

            if (bytesWritten == 0)
                break;

            offset += (int)bytesWritten;
        }
    }

    public void Resize(int columns, int rows)
    {
        if (disposed || pseudoConsole == IntPtr.Zero)
            return;

        var size = new COORD
        {
            X = (short)Math.Clamp(columns, 1, short.MaxValue),
            Y = (short)Math.Clamp(rows, 1, short.MaxValue)
        };

        int result = ResizePseudoConsole(pseudoConsole, size);

        if (result < 0)
        {
            throw new IOException(
                $"ResizePseudoConsole failed: HRESULT 0x{result:X8}");
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (pseudoConsole != IntPtr.Zero)
        {
            ClosePseudoConsole(pseudoConsole);
            pseudoConsole = IntPtr.Zero;
        }

        if (inputWrite != IntPtr.Zero)
        {
            CloseHandle(inputWrite);
            inputWrite = IntPtr.Zero;
        }

        if (outputRead != IntPtr.Zero)
        {
            CloseHandle(outputRead);
            outputRead = IntPtr.Zero;
        }

        if (threadHandle != IntPtr.Zero)
        {
            CloseHandle(threadHandle);
            threadHandle = IntPtr.Zero;
        }

        if (processHandle != IntPtr.Zero)
        {
            CloseHandle(processHandle);
            processHandle = IntPtr.Zero;
        }

        if (attributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int Length;
        public IntPtr SecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        ref SECURITY_ATTRIBUTES attributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr handle,
        byte[] buffer,
        uint size,
        out uint bytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr handle,
        byte[] buffer,
        uint size,
        out uint bytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(
        COORD size,
        IntPtr input,
        IntPtr output,
        uint flags,
        out IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(
        IntPtr pseudoConsole,
        COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int count,
        int flags,
        ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(
        IntPtr attributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref STARTUPINFOEX startupInfo,
        out PROCESS_INFORMATION processInformation);
}