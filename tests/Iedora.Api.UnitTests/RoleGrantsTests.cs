using Iedora.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.UnitTests;

[TestClass]
public sealed class RoleGrantsTests
{
    private static string[] Roles(string? spec, string? email) => new RoleGrants(spec).RolesFor(email).ToArray();

    [TestMethod]
    public void Exact_email_is_granted_its_role_and_others_are_not()
    {
        CollectionAssert.AreEquivalent(new[] { "admin" }, Roles("admin=me@x.com", "me@x.com"));
        Assert.AreEqual(0, Roles("admin=me@x.com", "other@x.com").Length);
    }

    [TestMethod]
    public void Matching_is_case_insensitive()
    {
        CollectionAssert.AreEquivalent(new[] { "admin" }, Roles("admin=Me@X.com", "ME@x.COM"));
    }

    [TestMethod]
    public void A_domain_suffix_grants_every_address_in_that_domain()
    {
        CollectionAssert.AreEquivalent(new[] { "staff" }, Roles("staff=@x.com", "anyone@x.com"));
        Assert.AreEqual(0, Roles("staff=@x.com", "anyone@y.com").Length);
    }

    [TestMethod]
    public void Multiple_roles_and_identities_parse_and_combine()
    {
        const string spec = "admin=a@x.com,b@x.com; staff=@x.com";
        CollectionAssert.AreEquivalent(new[] { "admin", "staff" }, Roles(spec, "a@x.com")); // exact admin + domain staff
        CollectionAssert.AreEquivalent(new[] { "staff" }, Roles(spec, "c@x.com"));           // domain only
    }

    [TestMethod]
    public void Empty_or_malformed_specs_grant_nothing()
    {
        foreach (var spec in new string?[] { null, "", "   ", "garbage", "=nobody" })
            Assert.AreEqual(0, Roles(spec, "me@x.com").Length, $"spec: {spec ?? "null"}");
    }

    [TestMethod]
    public void A_null_or_blank_email_is_granted_nothing()
    {
        Assert.AreEqual(0, Roles("admin=@x.com", null).Length);
        Assert.AreEqual(0, Roles("admin=@x.com", "  ").Length);
    }
}
