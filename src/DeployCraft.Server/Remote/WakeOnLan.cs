using System.Net;
using System.Net.Sockets;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Wake-up of a powered-off computer: "magic" packet (6 bytes of FF then the MAC address 16 times) sent over UDP
/// to the general broadcast and to the broadcast of the computer's subnet. The computer must have wake-on-LAN enabled
/// (BIOS and network card); across subnets, the router must let directed broadcast through.
/// </summary>
public static class WakeOnLan
{
    private static readonly int[] Ports = [9, 7];

    /// <summary>Wake-up packet for a MAC address "AA-BB-CC-DD-EE-FF" (separators "-", ":" or none).</summary>
    public static byte[] MagicPacket(string mac)
    {
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12) throw new ArgumentException($"Adresse MAC invalide : {mac}");
        var address = Convert.FromHexString(hex);
        var packet = new byte[6 + 16 * 6];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var i = 0; i < 16; i++) address.CopyTo(packet, 6 + i * 6);
        return packet;
    }

    /// <summary>Addresses to send the packet to: general broadcast, then those of the computer's known subnets.</summary>
    public static List<IPAddress> Destinations(IEnumerable<NetworkCard> cards)
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        foreach (var card in cards)
            if (IPAddress.TryParse(card.Broadcast, out var address) && !list.Contains(address)) list.Add(address);
        return list;
    }

    public static void Send(IReadOnlyCollection<NetworkCard> cards)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var destinations = Destinations(cards);
        foreach (var card in cards)
        {
            var packet = MagicPacket(card.Mac);
            foreach (var address in destinations)
                foreach (var port in Ports)
                    udp.Send(packet, packet.Length, new IPEndPoint(address, port));
        }
    }
}
