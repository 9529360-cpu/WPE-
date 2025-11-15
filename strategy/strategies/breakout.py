from __future__ import annotations

from dataclasses import dataclass
from statistics import mean
from typing import Sequence

from ..base import IStrategy, StrategyContext, TradeSignal
from ..models import Candle, Position, PositionSide


@dataclass
class BreakoutParams:
    lookback: int = 20          # 回看区间长度
    volume_lookback: int = 20   # 成交量均值计算长度
    volume_factor: float = 1.2  # 当前量 > x * 均量 才认为有效突破
    risk_reward: float = 1.5    # 目标 R 倍止盈（例如 1.5R）
    stop_buffer: float = 0.001  # 止损缓冲比例，比如 0.1%
    min_range_pct: float = 0.002  # 区间太窄就不玩，避免噪音


class BreakoutStrategy(IStrategy):
    def __init__(self, params: BreakoutParams | None = None) -> None:
        self._params = params or BreakoutParams()

    @property
    def name(self) -> str:
        return "breakout_1m"

    def generate_signal(self, ctx: StrategyContext) -> TradeSignal:
        candles: Sequence[Candle] = ctx.candles
        pos: Position = ctx.position

        if len(candles) < max(self._params.lookback, self._params.volume_lookback) + 1:
            return self._none(ctx, "not_enough_candles")

        # 最近一根用于决策
        last = candles[-1]

        # 形成区间：最近 lookback 根（不含当前）
        window = candles[-1 - self._params.lookback : -1]
        highs = [c.high for c in window]
        lows = [c.low for c in window]
        vols = [c.volume for c in candles[-self._params.volume_lookback : -1]]

        if not highs or not lows or not vols:
            return self._none(ctx, "window_empty")

        hi = max(highs)
        lo = min(lows)
        avg_vol = mean(vols)
        rng = hi - lo
        if lo <= 0 or rng <= 0:
            return self._none(ctx, "invalid_range")

        range_pct = rng / last.close
        if range_pct < self._params.min_range_pct:
            # 区间太小，认为是纯噪音，避免乱开仓
            return self._none(ctx, "range_too_small")

        # 放量条件
        if last.volume < avg_vol * self._params.volume_factor:
            return self._none(ctx, "volume_not_enough")

        # 已持仓 → 管理仓位（止盈/止损）
        if not pos.is_flat():
            return self._manage_open_position(ctx, last, hi, lo)

        # 无仓位 → 判断是否突破开仓
        if last.close > hi:
            # 向上突破，尝试开多
            return TradeSignal(
                symbol=ctx.symbol,
                side="LONG",
                reason="breakout_up",
                strategy_name=self.name,
                confidence=0.7,
            )

        if last.close < lo:
            # 向下跌破，尝试开空
            return TradeSignal(
                symbol=ctx.symbol,
                side="SHORT",
                reason="breakout_down",
                strategy_name=self.name,
                confidence=0.7,
            )

        return self._none(ctx, "no_breakout")

    def _manage_open_position(
        self,
        ctx: StrategyContext,
        last: Candle,
        hi: float,
        lo: float
    ) -> TradeSignal:
        pos = ctx.position
        entry = pos.entry_price
        price = last.close

        # 定义 1R = entry 与区间边界之间的距离
        if pos.side == "LONG":
            risk = max(entry - lo, 1e-6)
            r_multiple = (price - entry) / risk
        elif pos.side == "SHORT":
            risk = max(hi - entry, 1e-6)
            r_multiple = (entry - price) / risk
        else:
            return self._none(ctx, "unexpected_flat")

        # 止盈：达到 target R
        if r_multiple >= self._params.risk_reward:
            return TradeSignal(
                symbol=ctx.symbol,
                side="CLOSE",
                reason=f"take_profit_{r_multiple:.2f}R",
                strategy_name=self.name,
                confidence=0.9,
            )

        # 止损：跌破/突破关键位
        if pos.side == "LONG":
            stop = lo * (1 - self._params.stop_buffer)
            if price <= stop:
                return TradeSignal(
                    symbol=ctx.symbol,
                    side="CLOSE",
                    reason="stop_loss_long",
                    strategy_name=self.name,
                    confidence=0.9,
                )
        elif pos.side == "SHORT":
            stop = hi * (1 + self._params.stop_buffer)
            if price >= stop:
                return TradeSignal(
                    symbol=ctx.symbol,
                    side="CLOSE",
                    reason="stop_loss_short",
                    strategy_name=self.name,
                    confidence=0.9,
                )

        return self._none(ctx, "hold_position")

    def _none(self, ctx: StrategyContext, reason: str) -> TradeSignal:
        return TradeSignal(
            symbol=ctx.symbol,
            side="NONE",
            reason=reason,
            strategy_name=self.name,
            confidence=0.0,
        )
