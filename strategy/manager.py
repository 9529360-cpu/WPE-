from __future__ import annotations

from dataclasses import dataclass
from typing import List, Sequence

from .base import IStrategy, StrategyContext, TradeSignal


@dataclass
class CombinedDecision:
    """多策略的综合决策结果"""
    symbol: str
    signals: List[TradeSignal]
    final_signal: TradeSignal


class StrategyManager:
    def __init__(self, strategies: Sequence[IStrategy]) -> None:
        self._strategies: List[IStrategy] = list(strategies)

    @property
    def strategies(self) -> Sequence[IStrategy]:
        return self._strategies

    def generate_decision(self, ctx: StrategyContext) -> CombinedDecision:
        """对同一个 symbol/context，让所有策略发声，然后做个简单合成"""
        signals: List[TradeSignal] = []
        for s in self._strategies:
            sig = s.generate_signal(ctx)
            signals.append(sig)

        # 暂时用最简单的规则：
        # - 如果有 CLOSE，就优先 CLOSE
        # - 否则 LONG / SHORT 里选信心最高的
        # - 否则 NONE
        final = self._pick_final_signal(signals, ctx.symbol)
        return CombinedDecision(symbol=ctx.symbol, signals=signals, final_signal=final)

    @staticmethod
    def _pick_final_signal(signals: List[TradeSignal], symbol: str) -> TradeSignal:
        # 优先 CLOSE
        close_signals = [s for s in signals if s.side == "CLOSE"]
        if close_signals:
            # 随便选一个（也可以后面按优先级）
            return close_signals[0]

        directional = [s for s in signals if s.side in ("LONG", "SHORT")]
        if directional:
            # 选 confidence 最高的
            directional.sort(key=lambda s: s.confidence, reverse=True)
            return directional[0]

        # 全都 NONE
        return TradeSignal(
            symbol=symbol,
            side="NONE",
            reason="no_strong_signal",
            strategy_name="manager",
            confidence=0.0,
        )
