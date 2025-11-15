from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Literal, Optional


@dataclass(frozen=True)
class Candle:
    symbol: str
    open_time: datetime
    close_time: datetime
    open: float
    high: float
    low: float
    close: float
    volume: float


PositionSide = Literal["LONG", "SHORT", "FLAT"]


@dataclass
class Position:
    symbol: str
    side: PositionSide = "FLAT"
    qty: float = 0.0
    entry_price: float = 0.0

    def is_flat(self) -> bool:
        return self.side == "FLAT"

    def unrealized_pnl(self, last_price: float) -> float:
        if self.is_flat() or self.qty == 0:
            return 0.0
        if self.side == "LONG":
            return (last_price - self.entry_price) * self.qty
        else:  # SHORT
            return (self.entry_price - last_price) * self.qty


@dataclass
class AccountSnapshot:
    equity: float
    free_balance: float
    timestamp: datetime
