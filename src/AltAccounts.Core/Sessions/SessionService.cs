using System.Runtime.InteropServices;
using AltAccounts.Core.Native;

namespace AltAccounts.Core.Sessions;

public enum SessionStatus { SignedOut, Active, Locked, Disconnected, Unknown }

public sealed record SessionInfo(int SessionId, string UserName, string Domain, SessionStatus Status);

/// <summary>Live session status from WTSEnumerateSessions / WTSSessionInfoEx.</summary>
public static class SessionService
{
    public static int CurrentSessionId { get; } = System.Diagnostics.Process.GetCurrentProcess().SessionId;

    public static List<SessionInfo> GetSessions()
    {
        var list = new List<SessionInfo>();
        if (!Win32.WTSEnumerateSessions(Win32.WTS_CURRENT_SERVER_HANDLE, 0, 1, out var buffer, out int count))
            return list;
        try
        {
            int size = Marshal.SizeOf<Win32.WTS_SESSION_INFO>();
            for (int i = 0; i < count; i++)
            {
                var s = Marshal.PtrToStructure<Win32.WTS_SESSION_INFO>(buffer + i * size);
                if (s.SessionId == 0) continue; // services session
                var info = Query(s.SessionId);
                if (info is not null && info.UserName.Length > 0) list.Add(info);
            }
        }
        finally { Win32.WTSFreeMemory(buffer); }
        return list;
    }

    public static SessionInfo? Query(int id)
    {
        if (!Win32.WTSQuerySessionInformation(Win32.WTS_CURRENT_SERVER_HANDLE, id,
                Win32.WTS_INFO_CLASS.WTSSessionInfoEx, out var p, out _))
            return null;
        try
        {
            var ex = Marshal.PtrToStructure<Win32.WTSINFOEX>(p);
            if (ex.Level != 1) return null;
            var d = ex.Data;
            return new SessionInfo(id, d.UserName ?? "", d.DomainName ?? "", Map(d.SessionState, d.SessionFlags));
        }
        finally { Win32.WTSFreeMemory(p); }
    }

    public static SessionStatus Map(Win32.WTS_CONNECTSTATE_CLASS state, int flags) => state switch
    {
        Win32.WTS_CONNECTSTATE_CLASS.WTSDisconnected => SessionStatus.Disconnected,
        Win32.WTS_CONNECTSTATE_CLASS.WTSActive or Win32.WTS_CONNECTSTATE_CLASS.WTSConnected =>
            flags == Win32.WTS_SESSIONSTATE_LOCK ? SessionStatus.Locked : SessionStatus.Active,
        _ => SessionStatus.Unknown,
    };

    /// <summary>Best status for a username across sessions (Active beats Locked beats Disconnected).</summary>
    public static SessionStatus StatusFor(IEnumerable<SessionInfo> sessions, string userName)
    {
        var mine = sessions.Where(s => s.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count == 0) return SessionStatus.SignedOut;
        foreach (var st in new[] { SessionStatus.Active, SessionStatus.Locked, SessionStatus.Disconnected })
            if (mine.Any(s => s.Status == st)) return st;
        return SessionStatus.Unknown;
    }

    public static string Label(SessionStatus s) => s switch
    {
        SessionStatus.Active => "Active",
        SessionStatus.Locked => "Locked",
        SessionStatus.Disconnected => "Signed in (in background)",
        SessionStatus.SignedOut => "Signed out",
        _ => "Unknown",
    };

    /// <summary>Signs out a session. Needs admin for other users' sessions.</summary>
    public static void LogOff(int sessionId)
    {
        if (!Win32.WTSLogoffSession(Win32.WTS_CURRENT_SERVER_HANDLE, sessionId, true))
            throw new System.ComponentModel.Win32Exception();
    }

    /// <summary>Pro only: disconnects the current session so the sign-in screen appears.</summary>
    public static bool DisconnectCurrent() =>
        Win32.WTSDisconnectSession(Win32.WTS_CURRENT_SERVER_HANDLE, Win32.WTS_CURRENT_SESSION, false);
}
