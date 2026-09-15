// Host tag rules, as the server enforces them, so mistakes show beside the field.

export const MAX_TAGS = 20;
const TAG = /^[^\s,]{1,50}$/;

/** Tags are stored lowercase and once each. */
export function normalizeTags(tags: string[]): string[] {
  return [...new Set(tags.map((tag) => tag.trim().toLowerCase()).filter(Boolean))];
}

/** A form error for a list of tags, or null when they are fine. */
export function tagsError(tags: string[]): string | null {
  if (tags.length > MAX_TAGS) return `Up to ${MAX_TAGS} tags.`;
  return tags.every((tag) => TAG.test(tag))
    ? null
    : "Tags are up to 50 characters, without spaces or commas.";
}
