import { afterEach, describe, expect, it, vi } from "vitest";
import { api, request, ApiError } from "../services/api";
afterEach(() => vi.unstubAllGlobals());
describe("API transport", () => {
  it("sends the server token using the bearer header", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(JSON.stringify({ userCode: "GE-OWN-001001" }), {
          status: 200,
        }),
      );
    vi.stubGlobal("fetch", fetch);
    await api.me("test-token");
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5080/api/auth/me",
      expect.objectContaining({
        headers: expect.objectContaining({
          Authorization: "Bearer test-token",
        }),
      }),
    );
  });
  it("surfaces field validation from ProblemDetails", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(
            JSON.stringify({
              title: "Validation failed",
              errors: { Email: ["A valid email is required."] },
            }),
            { status: 400 },
          ),
        ),
    );
    await expect(request("/api/auth/register")).rejects.toMatchObject({
      message: "A valid email is required.",
      status: 400,
    });
  });
  it("handles unavailable networks without leaking fetch internals", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockRejectedValue(new Error("network internals")),
    );
    await expect(api.me("test-token")).rejects.toMatchObject({
      message: "Cannot reach G Energy. Check your connection and try again.",
      status: 0,
    });
  });
  it("handles non-JSON rate-limit responses", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response("", { status: 429 })),
    );
    await expect(request("/api/auth/login")).rejects.toEqual(
      new ApiError("Too many attempts. Please wait a minute.", 429),
    );
  });
});
