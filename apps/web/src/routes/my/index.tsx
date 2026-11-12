import { createFileRoute } from "@tanstack/react-router";
import { requireRoles } from "#/features/auth/session";
import { MyPage } from "#/features/my/MyPage";

export const Route = createFileRoute("/my/")({
	beforeLoad: ({ context }) => requireRoles(context.me, "pracownik"),
	component: MyPage,
});
