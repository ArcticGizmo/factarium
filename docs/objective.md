# Objective
Factarium is designed to give you quick, transparent, dev-centric metrics so you can see how you, your project, team or organization are doing. Start with a single database and ONLY scale out to additional services if you actually needed them. No more starting 17 services to just see if you like the data or not.

# Core Tenants
```
sync - transform - aggregate - render
```
These 4 stages are the entire model. 
1. Sync your data from where you have it
2. Transform those raw, stored records into something you can acctually use
3. Aggregate records into materialised metrics you actually want to query over
4. Render with dashboards

# Features

## Sync and hold
There are lots of systems around and having to deal with querying them when you want information is often the annoying bottleneck (rate limits, pricing, weird formats). Factarium is designed to replicate the information you need for as long as you need it. Once you have your facts, you then run the aggregates/transforms you want right within your service.

## Aggregate/Transforms through workers
Because we are built on postgres, it is not the most OLAP of choices, but we can still materialise what we need for efficient queries. Once you have your data sources defined and replicated, you then create concurrent workers that will perform transforms or aggregations on the schedule you set. You can even transform your transforms so you don't have to do all the heavy lifting every time. 

Metrics love timeseries and so do we. Define exactly how old information needs to be before even bothering to calculate it and save some cycles. We also allow you to configure the priority, weighting and concurrency of the transforms you run. Just want to run the github integration right now? You can do that. Having a break and have some spare cycles? Swap your pipeline profile and we will run those real heavy workloads for you. 

## Annotations and snapshots
When was the last time you saw a dashboard and went "hmmm, that doesnt seem right" or "oh that is fine, we did xyz so it should be ignored" and it just became a message in slack that never came up again after your sprint review. Factarium allows you to annotate and **snapshot** the metrics that you generate so that you can give your reasons when something seems off or let everyone know you suspect the data is wrong. This does not just point to a particular dashboard, it keeps a copy of the data you just viewed so that your comment always gets context, even after the data has been cleaned up or new metrics generated.

## We also have live metrics
Looking at historical information is good, but you also need to see how things are going right now. If you are in the middle of the sprint and you only have 10% of tickets in done, you are probably going to be having a bad time. That is why we support dashboards for transient information so you get the information you need all in one place.

## People are the core of the model
All data should be attributed to a person to make sense, but we all know that what a person "is" in every integration is going to be very different. Don't worry about it until you get your data in. Then you can map Integration X's representation of a person/user to our first class Person record. 

Someone has multiple github usernames (for some reason), just keep adding them and we will attribute all the previous data to that person during transformation.

# Technical Considerations
- must be easily shippable to dev centric users (standalone exe or development mode), with the ability to create a postgres instance in docker
- dotnet backbone with a vue fronend
  - .net 10
  - EF Core
  - Quartz.Net for background scheduling
    - ensure there is a way to view/manage schedules for pipelines
  - veutify 4 for the UI (negotiable depending on what we want to use as a charting library) 
- user identity from day 0
  - consider authZ/authN from the get go so it is easy to add in later. Ensure that local-only implementations allow easy user setup without authorization