import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import type { UserSummary } from "./types";

export type Role = UserSummary["role"];

export interface NewUser {
  email: string;
  displayName: string;
  password: string;
  role: Role;
}

export interface UserChanges {
  displayName: string;
  role: Role;
}

export const userKeys = {
  all: ["users"] as const,
};

/** Everyone who can sign in to this server. Administrators only. */
export function useUsers() {
  return useQuery({
    queryKey: userKeys.all,
    queryFn: () => api.get<UserSummary[]>("/api/users"),
  });
}

function useUserMutation<TData, TVariables>(mutationFn: (variables: TVariables) => Promise<TData>) {
  const client = useQueryClient();
  return useMutation<TData, ApiError, TVariables>({
    mutationFn,
    onSuccess: () => void client.invalidateQueries({ queryKey: userKeys.all }),
  });
}

export const useCreateUser = () =>
  useUserMutation((user: NewUser) => api.post<UserSummary>("/api/users", user));

export const useUpdateUser = () =>
  useUserMutation(({ id, changes }: { id: string; changes: UserChanges }) =>
    api.put<UserSummary>(`/api/users/${id}`, changes),
  );

export const useSetUserDisabled = () =>
  useUserMutation(({ id, disabled }: { id: string; disabled: boolean }) =>
    api.post(`/api/users/${id}/${disabled ? "disable" : "enable"}`),
  );

export const useResetPassword = () =>
  useUserMutation(({ id, newPassword }: { id: string; newPassword: string }) =>
    api.post(`/api/users/${id}/reset-password`, { newPassword }),
  );

export const useDeleteUser = () => useUserMutation((id: string) => api.delete(`/api/users/${id}`));
