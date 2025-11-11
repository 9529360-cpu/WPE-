using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace 币安量化机器人.Services;

/// <summary>
/// 遗传算法参数优化器
/// </summary>
/// <remarks>
/// 核心思想:
/// 1. 初始化种群 (随机生成参数组合)
/// 2. 评估适应度 (运行回测计算收益)
/// 3. 选择 (保留优秀个体)
/// 4. 交叉 (组合优秀基因)
/// 5. 变异 (引入随机性)
/// 6. 迭代进化 (重复 2-5)
/// </remarks>
public sealed class GeneticAlgorithmOptimizer
{
    private readonly Random _random = new(42);

    /// <summary>
    /// 优化参数
    /// </summary>
    /// <param name="parameterSpace">参数空间定义</param>
    /// <param name="fitnessFunction">适应度函数 (参数 -> 得分)</param>
    /// <param name="populationSize">种群大小</param>
    /// <param name="generations">迭代代数</param>
    /// <param name="mutationRate">变异率</param>
    /// <param name="eliteCount">精英数量 (直接保留到下一代)</param>
    /// <returns>最优参数组合</returns>
    public async Task<OptimizationResult> OptimizeAsync(
        Dictionary<string, ParameterRange> parameterSpace,
        Func<Dictionary<string, double>, Task<double>> fitnessFunction,
        int populationSize = 50,
        int generations = 100,
        double mutationRate = 0.1,
        int eliteCount = 5,
        IProgress<OptimizationProgress>? progress = null)
    {
        LogService.Info("[GeneticOptimizer] 开始遗传算法优化: 种群={Pop}, 代数={Gen}, 变异率={Mut:P0}",
            populationSize, generations, mutationRate);

        // 1. 初始化种群
        List<Individual> population = InitializePopulation(parameterSpace, populationSize);
        var bestIndividual = new Individual();
        double bestFitness = double.MinValue;
        var history = new List<GenerationStats>();

        // 2. 迭代进化
        for (int gen = 0; gen < generations; gen++)
        {
            // 评估适应度
            await EvaluateFitnessAsync(population, fitnessFunction);

            // 排序 (适应度从高到低)
            population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));

            // 更新最佳个体
            if (population[0].Fitness > bestFitness)
            {
                bestFitness = population[0].Fitness;
                bestIndividual = population[0].Clone();
                LogService.Info("[GeneticOptimizer] 发现更优解: 代数={Gen}, 适应度={Fitness:F4}",
                    gen, bestFitness);
            }

            // 记录统计
            var stats = new GenerationStats
            {
                Generation = gen,
                BestFitness = population[0].Fitness,
                AverageFitness = population.Average(i => i.Fitness),
                WorstFitness = population[^1].Fitness
            };
            history.Add(stats);

            // 报告进度
            progress?.Report(new OptimizationProgress
            {
                CurrentGeneration = gen,
                TotalGenerations = generations,
                BestFitness = bestFitness,
                AverageFitness = stats.AverageFitness
            });

            // 如果是最后一代,跳过选择和繁殖
            if (gen == generations - 1)
            {
                break;
            }

            // 3. 选择 + 交叉 + 变异
            var newPopulation = new List<Individual>();

            // 精英保留
            for (int i = 0; i < eliteCount; i++)
            {
                newPopulation.Add(population[i].Clone());
            }

            // 繁殖新个体
            while (newPopulation.Count < populationSize)
            {
                Individual parent1 = SelectParent(population);
                Individual parent2 = SelectParent(population);
                Individual child = Crossover(parent1, parent2, parameterSpace);
                Mutate(child, parameterSpace, mutationRate);
                newPopulation.Add(child);
            }

            population = newPopulation;
        }

        LogService.Info("[GeneticOptimizer] 优化完成: 最优适应度={Fitness:F4}", bestFitness);

        return new OptimizationResult
        {
            BestParameters = bestIndividual.Genes,
            BestFitness = bestFitness,
            GenerationHistory = history,
            TotalEvaluations = populationSize * generations
        };
    }

    /// <summary>
    /// 初始化种群
    /// </summary>
    private List<Individual> InitializePopulation(
        Dictionary<string, ParameterRange> parameterSpace,
        int populationSize)
    {
        var population = new List<Individual>();

        for (int i = 0; i < populationSize; i++)
        {
            var genes = new Dictionary<string, double>();

            foreach ((string? name, ParameterRange? range) in parameterSpace)
            {
                genes[name] = GenerateRandomValue(range);
            }

            population.Add(new Individual { Genes = genes });
        }

        return population;
    }

    /// <summary>
    /// 评估种群适应度
    /// </summary>
    private async Task EvaluateFitnessAsync(
        List<Individual> population,
        Func<Dictionary<string, double>, Task<double>> fitnessFunction)
    {
        IEnumerable<Task> tasks = population.Select(async individual =>
        {
            if (individual.Fitness == 0) // 未评估过
            {
                individual.Fitness = await fitnessFunction(individual.Genes);
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 选择父代 (锦标赛选择)
    /// </summary>
    private Individual SelectParent(List<Individual> population, int tournamentSize = 3)
    {
        var tournament = new List<Individual>();

        for (int i = 0; i < tournamentSize; i++)
        {
            int index = _random.Next(population.Count);
            tournament.Add(population[index]);
        }

        return tournament.OrderByDescending(i => i.Fitness).First();
    }

    /// <summary>
    /// 交叉 (单点交叉)
    /// </summary>
    private Individual Crossover(
        Individual parent1,
        Individual parent2,
        Dictionary<string, ParameterRange> parameterSpace)
    {
        var child = new Individual();
        string[] parameterNames = parameterSpace.Keys.ToArray();
        int crossoverPoint = _random.Next(parameterNames.Length);

        for (int i = 0; i < parameterNames.Length; i++)
        {
            string paramName = parameterNames[i];
            child.Genes[paramName] = i < crossoverPoint
                ? parent1.Genes[paramName]
                : parent2.Genes[paramName];
        }

        return child;
    }

    /// <summary>
    /// 变异
    /// </summary>
    private void Mutate(
        Individual individual,
        Dictionary<string, ParameterRange> parameterSpace,
        double mutationRate)
    {
        foreach ((string? name, ParameterRange? range) in parameterSpace)
        {
            if (_random.NextDouble() < mutationRate)
            {
                individual.Genes[name] = GenerateRandomValue(range);
            }
        }
    }

    /// <summary>
    /// 生成随机值
    /// </summary>
    private double GenerateRandomValue(ParameterRange range)
    {
        if (range.IsInteger)
        {
            return _random.Next((int)range.Min, (int)range.Max + 1);
        }
        else
        {
            return range.Min + _random.NextDouble() * (range.Max - range.Min);
        }
    }
}

/// <summary>
/// 个体 (基因型)
/// </summary>
public class Individual
{
    public Dictionary<string, double> Genes { get; set; } = new();
    public double Fitness { get; set; }

    public Individual Clone()
    {
        return new Individual
        {
            Genes = new Dictionary<string, double>(Genes),
            Fitness = Fitness
        };
    }
}

/// <summary>
/// 参数范围
/// </summary>
public class ParameterRange
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Step { get; set; } = 1;
    public bool IsInteger { get; set; }
}

/// <summary>
/// 优化结果
/// </summary>
public class OptimizationResult
{
    public Dictionary<string, double> BestParameters { get; set; } = new();
    public double BestFitness { get; set; }
    public List<GenerationStats> GenerationHistory { get; set; } = new();
    public int TotalEvaluations { get; set; }
}

/// <summary>
/// 代统计
/// </summary>
public class GenerationStats
{
    public int Generation { get; set; }
    public double BestFitness { get; set; }
    public double AverageFitness { get; set; }
    public double WorstFitness { get; set; }
}

/// <summary>
/// 优化进度
/// </summary>
public class OptimizationProgress
{
    public int CurrentGeneration { get; set; }
    public int TotalGenerations { get; set; }
    public double BestFitness { get; set; }
    public double AverageFitness { get; set; }

    public double ProgressPercent => (double)CurrentGeneration / TotalGenerations * 100;
}
