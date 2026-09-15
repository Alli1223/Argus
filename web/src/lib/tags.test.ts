import { describe, expect, it } from "vitest";
import { normalizeTags, tagsError } from "./tags";

describe("normalizeTags", () => {
  it("lowercases, trims and removes repeats", () => {
    expect(normalizeTags([" Prod", "web", "prod", ""])).toEqual(["prod", "web"]);
  });
});

describe("tagsError", () => {
  it("accepts tags the server accepts", () => {
    expect(tagsError(["prod", "eu-west-1", "db:primary"])).toBeNull();
  });

  it("explains what is wrong", () => {
    expect(tagsError(["a b"])).toMatch(/without spaces or commas/);
    expect(tagsError(["x".repeat(51)])).toMatch(/up to 50 characters/);
    expect(tagsError(Array.from({ length: 21 }, (_, index) => `t${index}`))).toBe("Up to 20 tags.");
  });
});
