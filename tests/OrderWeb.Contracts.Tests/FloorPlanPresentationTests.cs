using OrderWeb.Contracts.Floors;
using Xunit;

namespace OrderWeb.Contracts.Tests;

public class FloorPlanPresentationTests
{
    [Theory]
    [InlineData(false, false, false, FloorTableVisualState.Free)]
    [InlineData(true, false, false, FloorTableVisualState.Occupied)]
    [InlineData(false, true, false, FloorTableVisualState.Problem)]
    [InlineData(true, false, true, FloorTableVisualState.Problem)]
    [InlineData(false, false, true, FloorTableVisualState.Problem)]
    public void ResolveState_matches_mother_status_colours(
        bool hasSession,
        bool reserved,
        bool problem,
        FloorTableVisualState expected)
    {
        var actual = FloorTableVisualStyles.ResolveState(hasSession, reserved, problem);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ColorsFor_uses_mother_green_amber_red_palette()
    {
        var free = FloorTableVisualStyles.ColorsFor(FloorTableVisualState.Free);
        var occupied = FloorTableVisualStyles.ColorsFor(FloorTableVisualState.Occupied);
        var problem = FloorTableVisualStyles.ColorsFor(FloorTableVisualState.Problem);

        Assert.Equal("#D1FAE5", free.Background);
        Assert.Equal("#10B981", free.Border);
        Assert.Equal("#FEF3C7", occupied.Background);
        Assert.Equal("#F59E0B", occupied.Border);
        Assert.Equal("#FEE2E2", problem.Background);
        Assert.Equal("#EF4444", problem.Border);
    }

    [Fact]
    public void SizeFor_square_matches_mother_tile_and_rectangle_is_wider()
    {
        var square = FloorTableVisualStyles.SizeFor(FloorTableShapeKind.Square);
        var rectangle = FloorTableVisualStyles.SizeFor(FloorTableShapeKind.Rectangle);

        Assert.Equal(120, square.Width);
        Assert.Equal(120, square.Height);
        Assert.True(rectangle.Width > square.Width);
        Assert.True(rectangle.Height < square.Height);
    }

    [Fact]
    public void Client_offline_capabilities_mark_stale_and_block_move_merge()
    {
        var offline = FloorPlanCapabilities.ClientOffline;
        Assert.True(offline.ShowStaleWarning);
        Assert.False(offline.AllowMove);
        Assert.False(offline.AllowMerge);
        Assert.True(offline.AllowOpen);
    }

    [Fact]
    public void Mother_admin_capabilities_allow_layout_and_background_edit()
    {
        var admin = FloorPlanCapabilities.MotherAdmin;
        Assert.True(admin.AllowLayoutEdit);
        Assert.True(admin.AllowBackgroundEdit);
        Assert.True(admin.ShowAdminTools);
        Assert.False(admin.ShowStaleWarning);
    }
}
