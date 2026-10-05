using Windows.Devices.Enumeration;

namespace DynamicIsland;

static class Headset
{
    public const int Unknown = -1;

    const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    const byte FullCharge = 100;
    static readonly string[] RequestedProperties = [BatteryProperty];

    public static async Task<int> ChargeAsync(Guid container)
    {
        if (container == Guid.Empty) return Unknown;
        try
        {
            var nodes = await DeviceInformation.FindAllAsync(
                $"System.Devices.ContainerId:=\"{container:B}\"", RequestedProperties, DeviceInformationKind.Device);
            foreach (DeviceInformation node in nodes)
            {
                if (node.Properties.TryGetValue(BatteryProperty, out object? level) && level is byte percent && percent <= FullCharge)
                    return percent;
            }
        }
        catch { }
        return Unknown;
    }
}
