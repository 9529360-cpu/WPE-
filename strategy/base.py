from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Protocol, Literal, Sequence, Optional

from .models import Candle, Position, AccountSnapshot


SignalSide = Literal["LONG", "SHORT", "CLOSE", "NONE"]


@dataclass
class StrategyContext:
    symbol: str
    now: datetime
    candles: Sequence[Candle]
    position: Position
    account: Optional[AccountSnapshot] = None


@dataclass
class TradeSignal:
    symbol: str
    side: SignalSide
    reason: str
    strategy_name: str
    confidence: float = 0.0


class IStrategy(Protocol):
    """所有策略都要实现同一个接口"""

    @property
    def name(self) -> str:
        ...

    def generate_signal(self, ctx: StrategyContext) -> TradeSignal:
        ...
