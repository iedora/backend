using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

public sealed record ImageUploadWire(string publicUrl);

// The Media service in isolation: POST /media/images (authenticated, tenant-scoped) + GET /media/{key}.
[TestClass]
public sealed class MediaUploadTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private async Task<HttpResponseMessage> UploadImage(byte[] bytes, string contentType, string? token)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "file", "f.png");
        var req = new HttpRequestMessage(HttpMethod.Post, "/media/images") { Content = form };
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(req);
    }

    [TestMethod]
    public async Task Upload_returns_a_tenant_scoped_url_and_serves_it()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@media.pt", Pw);

        var resp = await UploadImage(Png, "image/png", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var url = (await resp.Content.ReadFromJsonAsync<ImageUploadWire>())!.publicUrl;
        Assert.IsTrue(url.StartsWith($"/media/t/{tenantId}/", StringComparison.Ordinal), url);

        var served = await Client.GetAsync(url); // anonymous
        Assert.AreEqual(HttpStatusCode.OK, served.StatusCode);
        Assert.AreEqual("image/png", served.Content.Headers.ContentType!.MediaType);
        CollectionAssert.AreEqual(Png, await served.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task A_non_image_payload_is_rejected()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@mbad.pt", Pw);
        var resp = await UploadImage("not really a png"u8.ToArray(), "image/png", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task An_oversize_image_is_rejected()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@mbig.pt", Pw);
        var big = new byte[5 * 1024 * 1024 + 1]; // cap is 5 MiB
        Png.CopyTo(big, 0);
        var resp = await UploadImage(big, "image/png", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Uploading_requires_authentication()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await UploadImage(Png, "image/png", token: null)).StatusCode);
    }

    [TestMethod]
    public async Task Uploading_requires_a_tenant_scoped_token()
    {
        var noTenant = await RegisterAndLogin("owner@notenant.pt", Pw); // no tenant → no tid claim
        Assert.AreEqual(HttpStatusCode.BadRequest, (await UploadImage(Png, "image/png", noTenant.accessToken)).StatusCode);
    }

    [TestMethod]
    public async Task A_traversal_key_is_never_served()
    {
        var resp = await Client.GetAsync("/media/..%2f..%2f..%2fetc%2fpasswd");
        Assert.AreNotEqual(HttpStatusCode.OK, resp.StatusCode);
    }
}
