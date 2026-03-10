use std::collections::HashMap;

#[derive(Clone, Debug, Default)]
pub struct InstrumentState {
    pub bid: Option<f64>,
    pub ask: Option<f64>,
    pub last: Option<f64>,
}

#[derive(Clone, Debug, Default)]
pub struct MarketState {
    by_instrument: HashMap<String, InstrumentState>,
}

impl MarketState {
    pub fn update_quote(&mut self, instrument: &str, bid: Option<f64>, ask: Option<f64>, last: Option<f64>) {
        let entry = self.by_instrument.entry(instrument.to_string()).or_default();
        if let Some(value) = bid {
            entry.bid = Some(value);
        }
        if let Some(value) = ask {
            entry.ask = Some(value);
        }
        if let Some(value) = last {
            entry.last = Some(value);
        }
    }

    pub fn get(&self, instrument: &str) -> Option<&InstrumentState> {
        self.by_instrument.get(instrument)
    }
}
