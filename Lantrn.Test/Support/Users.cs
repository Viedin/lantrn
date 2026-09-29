using System.Security.Claims;
using Lantrn.Infra;

namespace Lantrn.Test.Support;

public static class Users
{
    public static ClaimsPrincipal Guest { get; } = new(new ClaimsIdentity());

    public static ClaimsPrincipal SignedIn(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test"));

    public static ClaimsPrincipal Admin(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, Roles.Admin)], "Test"));
}
