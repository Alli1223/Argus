import { Badge } from "@mantine/core";
import { IconArrowUpCircle } from "@tabler/icons-react";
import { Link } from "react-router";
import { useCurrentUser } from "../api/auth";
import { useServerUpdate } from "../api/updates";

/** For administrators: a badge in the header when a newer Argus is out, leading to where it is installed. */
export function ServerUpdateBadge() {
  const me = useCurrentUser();
  const update = useServerUpdate(me.data?.isAdmin === true);

  if (!update.data?.updateAvailable || !update.data.latest) return null;

  return (
    <Badge
      component={Link}
      to="/settings"
      color="iris"
      variant="light"
      leftSection={<IconArrowUpCircle size={13} />}
      style={{ cursor: "pointer" }}
      aria-label={`Argus ${update.data.latest.version} is available`}
    >
      Argus {update.data.latest.version}
    </Badge>
  );
}
