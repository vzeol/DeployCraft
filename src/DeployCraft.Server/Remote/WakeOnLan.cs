using System.Net;
using System.Net.Sockets;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Réveil d'un poste éteint : paquet « magique » (6 octets FF puis 16 fois l'adresse MAC) envoyé en UDP
/// à la diffusion générale et à celle du sous-réseau du poste. Le poste doit avoir le réveil réseau activé
/// (BIOS et carte réseau) ; entre sous-réseaux, le routeur doit laisser passer la diffusion dirigée.
/// </summary>
public static class WakeOnLan
{
    private static readonly int[] Ports = [9, 7];

    /// <summary>Paquet de réveil pour une adresse MAC « AA-BB-CC-DD-EE-FF » (séparateurs « - », « : » ou aucun).</summary>
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

    /// <summary>Adresses où envoyer le paquet : diffusion générale, puis celles des sous-réseaux connus du poste.</summary>
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
