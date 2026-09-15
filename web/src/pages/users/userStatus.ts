import type { UserSummary } from "../../api/types";

export type UserStatus = "Active" | "Disabled" | "Locked out";

/** Whether someone can sign in, and if not, why. */
export function userStatus(user: Pick<UserSummary, "isDisabled" | "isLockedOut">): UserStatus {
  if (user.isDisabled) return "Disabled";
  return user.isLockedOut ? "Locked out" : "Active";
}
