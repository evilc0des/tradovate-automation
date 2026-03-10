use anyhow::Error;
use tracing::error;

#[derive(Debug, Clone, Copy)]
pub enum ErrorKind {
    Config,
    Parse,
    Protocol,
    Transport,
}

impl ErrorKind {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Config => "config",
            Self::Parse => "parse",
            Self::Protocol => "protocol",
            Self::Transport => "transport",
        }
    }
}

pub fn log_error(kind: ErrorKind, context: &str, err: &Error) {
    error!(kind = kind.as_str(), context, error = %err, "classified error");
}

pub fn log_message(kind: ErrorKind, context: &str, detail: &str) {
    error!(kind = kind.as_str(), context, detail, "classified error detail");
}
