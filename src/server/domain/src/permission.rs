#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Permissions(pub u64);

impl Permissions {
    pub const VIEW_CHANNEL: Self = Self(1 << 0);
    pub const SEND_MESSAGE: Self = Self(1 << 1);
    pub const MANAGE_MESSAGES: Self = Self(1 << 2);
    pub const CONNECT_VOICE: Self = Self(1 << 3);
    pub const SPEAK: Self = Self(1 << 4);
    pub const STREAM: Self = Self(1 << 5);
    pub const MANAGE_CHANNEL: Self = Self(1 << 6);
    pub const MANAGE_ROLE: Self = Self(1 << 7);
    pub const KICK_MEMBER: Self = Self(1 << 8);
    pub const BAN_MEMBER: Self = Self(1 << 9);
    pub const ADMINISTRATOR: Self = Self(1 << 10);

    pub fn contains(self, required: Self) -> bool {
        self.0 & Self::ADMINISTRATOR.0 != 0 || self.0 & required.0 == required.0
    }

    pub fn resolve(
        base: Self,
        roles: &[Self],
        everyone: Override,
        role_overrides: &[Override],
        member: Override,
    ) -> Self {
        let mut value = roles.iter().fold(base.0, |v, r| v | r.0);
        if value & Self::ADMINISTRATOR.0 != 0 {
            return Self(u64::MAX);
        }
        value = (value & !everyone.deny.0) | everyone.allow.0;
        let deny = role_overrides.iter().fold(0, |v, r| v | r.deny.0);
        let allow = role_overrides.iter().fold(0, |v, r| v | r.allow.0);
        value = (value & !deny) | allow;
        Self((value & !member.deny.0) | member.allow.0)
    }
}

#[derive(Debug, Clone, Copy)]
pub struct Override {
    pub allow: Permissions,
    pub deny: Permissions,
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn member_override_wins_and_admin_bypasses() {
        let empty = Override {
            allow: Permissions(0),
            deny: Permissions(0),
        };
        let deny = Override {
            allow: Permissions(0),
            deny: Permissions::SEND_MESSAGE,
        };
        assert!(
            !Permissions::resolve(Permissions::SEND_MESSAGE, &[], empty, &[], deny)
                .contains(Permissions::SEND_MESSAGE)
        );
        assert!(
            Permissions::resolve(Permissions::ADMINISTRATOR, &[], deny, &[], deny)
                .contains(Permissions::SEND_MESSAGE)
        );
    }
}
