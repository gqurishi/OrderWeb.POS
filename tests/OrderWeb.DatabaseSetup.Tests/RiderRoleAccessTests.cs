using POS_in_NET.Models;
using POS_in_NET.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class RiderRoleAccessTests
{
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void RiderBoard_IsAvailableToEveryAuthenticatedRole(UserRole role)
    {
        var access = new RoleAccessService();

        Assert.True(access.CanAccessRoute(role, "weborders"));
    }

    [Fact]
    public void RiderBoard_RemainsUnavailableWithoutAuthentication()
    {
        var access = new RoleAccessService();

        Assert.False(access.CanAccessRoute(null, "weborders"));
    }
}
