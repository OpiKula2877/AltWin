using AltAccounts.Core.Accounts;
using AltAccounts.Core.Config;

namespace AltAccounts.App.Services;

/// <summary>Idea 14: shares whose folder or account no longer exists. Read-only; works without admin.</summary>
public static class StaleScan
{
    public sealed record Item(string Path, string? Sid, string Description);

    public static List<Item> Find(SharedConfig cfg)
    {
        var items = new List<Item>();
        foreach (var share in cfg.Shares)
        {
            if (!Directory.Exists(share.Path))
            {
                items.Add(new Item(share.Path, null, $"Folder no longer exists: {share.Path}"));
                continue;
            }
            foreach (var g in share.Grants.Where(g => !LocalAccountService.SidExists(g.Sid)))
                items.Add(new Item(share.Path, g.Sid, $"Account '{g.UserName}' no longer exists (grant on {share.Path})"));
        }
        foreach (var sid in cfg.Policies.Keys.Where(s => !LocalAccountService.SidExists(s)))
            items.Add(new Item("", sid, $"Options for deleted account '{cfg.Policies[sid].UserName}'"));
        return items;
    }
}
