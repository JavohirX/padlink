using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NesReceiver;

/// <summary>
/// Helpers for discovering the best LAN IPv4 address to advertise via QR code.
/// Priority order:
///   1. Wireless / Wi-Fi adapters (most likely what the phone will reach)
///   2. Any other non-loopback, non-APIPA IPv4 address
///   3. Fallback: 127.0.0.1 (should never happen in practice)
/// </summary>
internal static class NetworkHelper
{
    /// <summary>
    /// Returns the best LAN IPv4 address as a string (e.g. "192.168.1.42").
    /// Prefers a wireless adapter; falls back to any non-loopback IPv4.
    /// </summary>
    public static string GetLocalIpAddress()
    {
        var candidates = new List<(int priority, string address)>();

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            // Skip loopback and tunnel interfaces
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            // Determine priority: Wi-Fi adapters get priority 0, everything else gets 1
            int priority = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1;

            foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue; // IPv4 only

                string ip = addr.Address.ToString();

                // Skip loopback and APIPA addresses
                if (ip.StartsWith("127.") || ip.StartsWith("169.254."))
                    continue;

                candidates.Add((priority, ip));
            }
        }

        if (candidates.Count == 0)
            return "127.0.0.1";

        // Sort by priority (lower = better), then pick the first
        candidates.Sort((a, b) => a.priority.CompareTo(b.priority));
        return candidates[0].address;
    }
}
