using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Principal;

namespace MFS100Enroll
{
    public class UserAccountInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string Sid { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public bool IsDisabled { get; set; }

        public override string ToString() =>
            $"{Caption} (SID: {Sid}){(IsCurrent ? " [Current User]" : "")}";
    }

    public static class AccountManager
    {
        public static List<UserAccountInfo> GetEligibleAccounts()
        {
            var list = new List<UserAccountInfo>();
            string currentSid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;

            try
            {
                var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Caption, SID, Disabled, LocalAccount FROM Win32_UserAccount WHERE LocalAccount = True");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? string.Empty;
                    string caption = obj["Caption"]?.ToString() ?? string.Empty;
                    string sid = obj["SID"]?.ToString() ?? string.Empty;
                    bool disabled = (bool)(obj["Disabled"] ?? false);

                    // Skip built-in disabled utility accounts unless they are the current user
                    if (disabled && sid != currentSid)
                        continue;

                    list.Add(new UserAccountInfo
                    {
                        Name = name,
                        Caption = caption,
                        Sid = sid,
                        IsCurrent = string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase),
                        IsDisabled = disabled
                    });
                }
            }
            catch (Exception)
            {
                // Fallback using WindowsIdentity if WMI is restricted
                var cur = WindowsIdentity.GetCurrent();
                list.Add(new UserAccountInfo
                {
                    Name = cur.Name.Split('\\').Last(),
                    Caption = cur.Name,
                    Sid = cur.User?.Value ?? string.Empty,
                    IsCurrent = true,
                    IsDisabled = false
                });
            }

            return list.OrderByDescending(a => a.IsCurrent).ThenBy(a => a.Name).ToList();
        }

        public static UserAccountInfo? FindAccount(string identifier)
        {
            var accounts = GetEligibleAccounts();
            return accounts.FirstOrDefault(a =>
                string.Equals(a.Name, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Caption, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Sid, identifier, StringComparison.OrdinalIgnoreCase));
        }

        public static UserAccountInfo GetCurrentAccount()
        {
            var cur = WindowsIdentity.GetCurrent();
            string sid = cur.User?.Value ?? string.Empty;
            return FindAccount(sid) ?? new UserAccountInfo
            {
                Name = cur.Name.Split('\\').Last(),
                Caption = cur.Name,
                Sid = sid,
                IsCurrent = true,
                IsDisabled = false
            };
        }
    }
}
