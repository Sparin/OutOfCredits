using SMBLibrary.Client;
using SMBLibrary;
using System.Net;
using FileAttributes = SMBLibrary.FileAttributes;
using System.Diagnostics.CodeAnalysis;

namespace OutOfCredits;

public static class ShorterProgram
{

    private static ISMBClient _smbClient = new SMB2Client();
    private static ISMBFileStore? _fileStore;

    private static PeriodicTimer _periodicTimer = new(TimeSpan.FromMilliseconds(200));

    private static Settings _settings = new(
        Domain: "Domain",
        Login: "Username",
        Password: "Password");

    private static SmbPath _smbPath = new(
        ServerName: "Server",
        ShareName: "Share");

    record Settings(string Domain, string Login, string Password);

    record SmbPath(string ServerName, string ShareName);

    static async Task Main(string[] args)
    {
        Connect();
        object? fileHandle = null;

        while (true)
        {
            Console.WriteLine($"Current available credits: {_smbClient.GetAvailableCredits()}");
            try
            {
                var ntStatus = _fileStore.CreateFile(
                    out fileHandle,
                    out _,
                    "file",
                    AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                    FileAttributes.Normal,
                    ShareAccess.None,
                    CreateDisposition.FILE_SUPERSEDE,
                    CreateOptions.FILE_NON_DIRECTORY_FILE,
                    null);

                if (ntStatus == NTStatus.STATUS_INVALID_SMB)
                    Console.WriteLine("Timeout exceeded? Connection lost?");
                else if (ntStatus != NTStatus.STATUS_SUCCESS)
                    throw new Exception($"Failed to create file on storage, NT_STATUS: {ntStatus}");
            }
            finally
            {
                if (fileHandle != null)
                    _fileStore.CloseFile(fileHandle);
            }

            Console.WriteLine("Wait for next tick");
            await _periodicTimer.WaitForNextTickAsync();
        }
    }

    [MemberNotNull(nameof(_fileStore))]
    private static void Connect()
    {
        if (_smbClient.IsConnected && _fileStore != null)
        {
            return;
        }

        _smbClient = new SMB2Client();
        var serverName = _smbPath.ServerName;
        var isIpAddressSpecified = IPAddress.TryParse(serverName, out var ipAddress);

        var isConnected = isIpAddressSpecified
            ? _smbClient.Connect(ipAddress, SMBTransportType.DirectTCPTransport)
            : _smbClient.Connect(serverName, SMBTransportType.DirectTCPTransport);

        if (!isConnected)
        {
            isConnected = isIpAddressSpecified
                ? _smbClient.Connect(ipAddress, SMBTransportType.NetBiosOverTCP)
                : _smbClient.Connect(serverName, SMBTransportType.NetBiosOverTCP);

            if (!isConnected)
            {
                throw new Exception($"Can not connect to server {serverName}");
            }
        }

        var status = _smbClient.Login(_settings.Domain, _settings.Login, _settings.Password);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new Exception($"Can not login to server as {_settings.Domain}\\{_settings.Login}, NT_STATUS: {status}");
        }

        var shareName = _smbPath.ShareName;
        _fileStore = _smbClient.TreeConnect(shareName, out status);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new Exception($"Can not connect to share \"{shareName}\", NT_STATUS: {status}");
        }
    }
}