using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BlastemReceiver;

internal static class NetworkHelper
{
    public static string GetLocalIpAddress()
    {
        var candidates = new List<(int priority, string address)>();

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            int priority = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1;

            foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                string ip = addr.Address.ToString();

                if (ip.StartsWith("127.") || ip.StartsWith("169.254."))
                    continue;

                candidates.Add((priority, ip));
            }
        }

        if (candidates.Count == 0)
            return "127.0.0.1";

        candidates.Sort((a, b) => a.priority.CompareTo(b.priority));
        return candidates[0].address;
    }
}
