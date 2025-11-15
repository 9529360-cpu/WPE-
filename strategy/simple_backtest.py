from __future__ import annotations

import csv
from dataclasses import dataclass
from datetime import datetime
from typing import List

from .base import StrategyContext
from .models import Candle, Position
from .strategies.breakout import BreakoutStrategy, BreakoutParams


@dataclass
class TradeRecord:
    symbol: str
    entry_time: datetime
    exit_time: datetime
    side: str
    entry_price: float
    exit_price: float
    qty: float
    pnl: float


def load_candles_from_csv(path: str, symbol: str) -> List[Candle]:
    candles: List[Candle] = []
    with open(path, "r", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            # 根据你自己的 CSV 字段名调整
            candles.append(
                Candle(
                    symbol=symbol,
                    open_time=datetime.fromisoformat(row["open_time"]),
                    close_time=datetime.fromisoformat(row["close_time"]),
                    open=float(row["open"]),
                    high=float(row["high"]),
                    low=float(row["low"]),
                    close=float(row["close"]),
                    volume=float(row["volume"]),
                )
            )
    return candles


def run_simple_backtest(
    candles: List[Candle],
    initial_equity: float = 1000.0,
    risk_per_trade_pct: float = 0.01,
) -> None:
    strategy = BreakoutStrategy(BreakoutParams())

    position = Position(symbol=candles[0].symbol)
    equity = initial_equity
    trades: List[TradeRecord] = []

    min_lookback = 30  # 至少要这么多根 K 线才开始跑策略

    for i in range(min_lookback, len(candles)):
        window = candles[: i + 1]
        last = window[-1]

        ctx = StrategyContext(
            symbol=last.symbol,
            now=last.close_time,
            candles=window,
            position=position,
            account=None,  # 简化处理
        )

        signal = strategy.generate_signal(ctx)

        # 简单的仓位与交易执行逻辑（单仓模式）
        if position.is_flat():
            if signal.side in ("LONG", "SHORT"):
                risk_amt = equity * risk_per_trade_pct
                # 用一个简单的“风险 1R ≈ 1% 价格波动”的粗估算
                price = last.close
                # 不考虑杠杆与精度，先跑逻辑
                qty = risk_amt / (price * 0.01)
                position.side = "LONG" if signal.side == "LONG" else "SHORT"
                position.entry_price = price
                position.qty = qty
        else:
            # 已有仓位
            if signal.side == "CLOSE":
                exit_price = last.close
                pnl = position.unrealized_pnl(exit_price)
                trades.append(
                    TradeRecord(
                        symbol=position.symbol,
                        entry_time=datetime.fromtimestamp(0),
                        exit_time=last.close_time,
                        side=position.side,
                        entry_price=position.entry_price,
                        exit_price=exit_price,
                        qty=position.qty,
                        pnl=pnl,
                    )
                )
                equity += pnl
                # 平仓
                position = Position(symbol=position.symbol)

        # 可以在这里记录 equity 曲线
        # print(last.close_time, equity)

    # 回测结果简单输出
    total_pnl = sum(t.pnl for t in trades)
    win_trades = [t for t in trades if t.pnl > 0]
    loss_trades = [t for t in trades if t.pnl <= 0]

    print(f"初始资金: {initial_equity:.2f}")
    print(f"结束资金: {equity:.2f}")
    print(f"总盈亏: {total_pnl:.2f}")
    print(f"交易次数: {len(trades)} 胜: {len(win_trades)} 负: {len(loss_trades)}")
    if trades:
        avg_win = sum(t.pnl for t in win_trades) / len(win_trades) if win_trades else 0
        avg_loss = sum(t.pnl for t in loss_trades) / len(loss_trades) if loss_trades else 0
        print(f"平均盈利: {avg_win:.2f}, 平均亏损: {avg_loss:.2f}")


if __name__ == "__main__":
    # 示例：你导出一个 BTCUSDT_1m.csv，列名按上面来配
    candles = load_candles_from_csv("BTCUSDT_1m.csv", "BTCUSDT")
    run_simple_backtest(candles)
