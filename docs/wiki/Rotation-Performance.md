# Rotation Performance

Rotation Performance identifies map and mode combinations that gain or lose players while a round is running.

![ServerPulse Rotation Performance](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/rotation-performance.png)

## How a pairing is measured

ServerPulse records the human population at round start and end, plus joins and leaves during the round. **Population change** is end population minus start population.

The table includes:

- friendly map and mode names;
- game and completed-round count;
- average starting and ending population;
- population change;
- joins and leaves;
- a sample-quality assessment.

## Filters

- **Reliable samples** have enough completed rounds to support comparison.
- **Gaining players** finish with a higher population on average.
- **Losing players** finish with a lower population on average.
- **All samples** includes early data that should be interpreted cautiously.

An often-played rotation is not automatically a good-retaining rotation. Use the reliable view to choose controlled rotation experiments, then compare the same combination again after the change.
