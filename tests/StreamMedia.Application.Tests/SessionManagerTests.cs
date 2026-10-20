using StreamMedia.Application.Streaming;

public class SessionManagerTests
{
    // Reloj manual: un tick por segundo.
    sealed class ManualClock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Now;
    }

    [Fact]
    public void Sesion_vencida_no_se_lee_ni_revive_y_Prune_libera_su_asset()
    {
        var clock = new ManualClock();
        var sessions = new SessionManager(clock);
        var id = sessions.Create("asset1").Id;

        clock.Now = 11 * 60; // dentro del TTL: el latido la renueva
        Assert.NotNull(sessions.Heartbeat(id));

        clock.Now += 12 * 60; // 12 minutos sin latido
        Assert.Null(sessions.Get(id));
        Assert.Null(sessions.Heartbeat(id));
        Assert.Contains("asset1", sessions.ReferencedAssetIds()); // sigue protegiendo hasta el Prune

        Assert.Equal(id, Assert.Single(sessions.Prune()));
        Assert.Empty(sessions.ReferencedAssetIds());
    }
}
