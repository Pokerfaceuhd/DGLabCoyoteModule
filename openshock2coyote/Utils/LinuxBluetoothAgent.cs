#if NET10_0_LINUX

using Tmds.DBus;

namespace openshock2coyote.Utils;

[DBusInterface("org.bluez.Agent1")]
public interface IBlueZAgent : IDBusObject
{
    Task ReleaseAsync();

    Task<string> RequestPinCodeAsync(
        ObjectPath device);

    Task DisplayPinCodeAsync(
        ObjectPath device,
        string pincode);

    Task<uint> RequestPasskeyAsync(
        ObjectPath device);

    Task DisplayPasskeyAsync(
        ObjectPath device,
        uint passkey,
        ushort entered);

    Task RequestConfirmationAsync(
        ObjectPath device,
        uint passkey);

    Task RequestAuthorizationAsync(
        ObjectPath device);

    Task AuthorizeServiceAsync(
        ObjectPath device,
        string uuid);

    Task CancelAsync();
}

[DBusInterface("org.bluez.AgentManager1")]
public interface IBlueZAgentManager : IDBusObject
{
    Task RegisterAgentAsync(
        ObjectPath agent,
        string capability);

    Task RequestDefaultAgentAsync(
        ObjectPath agent);

    Task UnregisterAgentAsync(
        ObjectPath agent);
}

public sealed class LinuxBluetoothAgent : IBlueZAgent
{
    private static readonly ObjectPath AgentPath =
        new("/com/openshock/BluetoothAgent");

    private readonly Connection _connection;

    private LinuxBluetoothAgent(Connection connection)
    {
        _connection = connection;
    }

    public static async Task<LinuxBluetoothAgent> RegisterAsync()
    {
        var connection = new Connection(Address.System);

        await connection.ConnectAsync();

        var agent = new LinuxBluetoothAgent(connection);

        await connection.RegisterObjectAsync(agent);

        var manager =
            connection.CreateProxy<IBlueZAgentManager>(
                "org.bluez",
                new ObjectPath("/org/bluez"));

        try
        {
            await manager.RegisterAgentAsync(
                AgentPath,
                "KeyboardDisplay");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to register BlueZ Bluetooth agent.",
                ex);
        }

        await manager.RequestDefaultAgentAsync(AgentPath);

        return agent;
    }

    public Task ReleaseAsync()
    {
        return Task.CompletedTask;
    }

    public Task<string> RequestPinCodeAsync(
        ObjectPath device)
    {
        throw new Exception(
            "org.bluez.Error.Rejected");
    }

    public Task DisplayPinCodeAsync(
        ObjectPath device,
        string pincode)
    {
        return Task.CompletedTask;
    }

    public Task<uint> RequestPasskeyAsync(
        ObjectPath device)
    {
        throw new Exception(
            "org.bluez.Error.Rejected");
    }

    public Task DisplayPasskeyAsync(
        ObjectPath device,
        uint passkey,
        ushort entered)
    {
        return Task.CompletedTask;
    }

    public Task RequestConfirmationAsync(
        ObjectPath device,
        uint passkey)
    {
        // Automatically accept the passkey.
        return Task.CompletedTask;
    }

    public Task RequestAuthorizationAsync(
        ObjectPath device)
    {
        return Task.CompletedTask;
    }

    public Task AuthorizeServiceAsync(
        ObjectPath device,
        string uuid)
    {
        return Task.CompletedTask;
    }

    public Task CancelAsync()
    {
        return Task.CompletedTask;
    }
}

#endif
