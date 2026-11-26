using System.Text.Json;
using WinCareDesktop.Services;
using Xunit;
namespace WinCareDesktop.Tests;
public class DnsSafetyTests
{
    [Fact]
    public void Dns_preserves_automatic_mode_and_refuses_external_edit_on_undo()
    {
        var type = typeof(SystemOptimizerService).Assembly.GetType("WinCareDesktop.Services.DnsSettingsService");
        Assert.NotNull(type);
        string state = "{\"AdapterId\":\"adapter-fixture\",\"Name\":\"Fixture\",\"Index\":7,\"Automatic\":true,\"Servers\":[\"10.0.0.1\",\"10.0.0.2\"]}";
        var history = TestService.New();
        dynamic service = Activator.CreateInstance(type, history, (Func<string, string>)(_ => state), (Action<string, string>)((_, s) => state = s))!;
        Assert.True((bool)service.Change("adapter-fixture", "1.1.1.1"));
        var tx = Assert.Single(history.GetUndoTransactions());
        Assert.True((bool)service.Restore(tx.UndoPayload));
        Assert.True(JsonDocument.Parse(state).RootElement.GetProperty("Automatic").GetBoolean());
        Assert.True((bool)service.Change("adapter-fixture", "1.1.1.1"));
        state = state.Replace("1.1.1.1", "9.9.9.9");
        Assert.False((bool)service.Restore(history.GetUndoTransactions()[0].UndoPayload));
        Assert.Contains("9.9.9.9", state);
    }
}
