using System.Reflection;
using SMBLibrary.Client;

namespace OutOfCredits;

public static class ISMBClientExtensions
{
    public static ushort? GetAvailableCredits(this ISMBClient client)
    {
        if (client is not SMB2Client)
            return null;

        var fieldInfo = typeof(SMB2Client).GetField(
            "m_availableCredits", 
            BindingFlags.Instance | BindingFlags.NonPublic);

        return (ushort?)fieldInfo.GetValue(client);
    }
}