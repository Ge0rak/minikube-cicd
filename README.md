# minikube-cicd

CI/CD automation for a .NET application, deployed to Kubernetes (Minikube) via Azure DevOps.

Significa DevOps Engineer technical assessment.

## Overview

This project implements a complete CI/CD pipeline that builds, tests, containerizes, and deploys a minimal ASP.NET Core Web API to a local Kubernetes (Minikube) cluster, using Azure DevOps Pipelines end to end. It also includes monitoring, autoscaling, health probes, and automated testing with both pre-merge and post-merge quality gates.

## Architecture

```
Developer (laptop)
    |
    +-- git push --------------> GitHub (public, PR reviews happen here)
    |                                 |
    |                                 +-- PR opened/updated --> Azure Pipeline #2
    |                                 |   (azure-pipelines-pr.yml, GitHub-sourced)
    |                                 |   Install SDK -> Restore -> Build -> Test
    |                                 |   Reports pass/fail as a GitHub status check
    |                                 |   (blocks merge if tests fail)
    |
    +-- git push azure main ---> Azure Repos (source of truth for CD)
                                      |
                                      v
                          Azure Pipeline #1 triggers
                          (azure-pipelines.yml)
                                      |
                +---------------------+---------------------+
                v                                             v
     Stage: BuildAndPush                            Stage: Deploy
     (Microsoft-hosted agent)                        (Self-hosted agent,
          |                                            runs on laptop)
1. Install .NET 10 SDK                            1. kubectl apply -f k8s/
2. Restore dependencies                              (Deployment, Service, HPA)
3. Build                                          2. kubectl rollout status
4. Run tests (xUnit, 5 tests)                            |
   - blocks pipeline if failing                          v
5. Docker build (multi-stage)                   Minikube cluster (local)
6. Push image -> Docker Hub                            |
   (eograk/sampleapp)                                  +-- Pulls image from Docker Hub
          |                                            +-- 2-5 Pods (HPA-managed)
          +----------------> Docker Hub                +-- Liveness/Readiness/Startup
                             (public registry)          |    probes on /health
                                                         +-- NodePort Service (30080)
                                                              |
                                                              v
                                                   App reachable locally

                          Monitoring (parallel, in-cluster)
                          -----------------------------------
                          Prometheus + Grafana (Helm, namespace: monitoring)
                          Scrapes CPU/Memory from all pods; visualized via
                          Grafana dashboards (localhost:3000 via port-forward)
```

## Repository structure

```
minikube-cicd/
├── azure-pipelines.yml          # Main CD pipeline (Azure Repos-sourced)
├── azure-pipelines-pr.yml       # PR validation pipeline (GitHub-sourced)
├── Dockerfile                   # Multi-stage build for the API
├── .dockerignore
├── .gitignore
├── SampleApp/                   # ASP.NET Core 10 Web API
│   ├── Program.cs               # Endpoints: /health, /weatherforecast
│   └── SampleApp.csproj
├── SampleApp.Tests/             # xUnit integration tests
│   ├── ApiTests.cs
│   └── SampleApp.Tests.csproj
└── k8s/                         # Kubernetes manifests
    ├── deployment.yaml          # 2-5 replicas, probes, resource requests/limits
    ├── service.yaml             # NodePort, port 30080
    └── hpa.yaml                 # HorizontalPodAutoscaler (70% CPU / 80% Memory)
```

## The application

A minimal ASP.NET Core 10 Web API with two endpoints:

- `GET /health` — returns `{"status":"Healthy"}`, used by Kubernetes liveness/readiness/startup probes
- `GET /weatherforecast` — returns 5 sample weather data items (from the default .NET Web API template)

## CI/CD pipeline

Two separate Azure Pipelines work together:

| Pipeline | Source | Trigger | What it does |
|---|---|---|---|
| `azure-pipelines.yml` | Azure Repos | Push to `main` | Full build, test, Docker build/push, and deploy to Minikube |
| `azure-pipelines-pr.yml` | GitHub | Pull Request to `main` | Build and test only — acts as a merge gate on GitHub |

The main pipeline runs in two stages:

1. **BuildAndPush** (Microsoft-hosted agent) — installs the .NET SDK, restores, builds, runs the test suite, builds a multi-stage Docker image, and pushes it to Docker Hub.
2. **Deploy** (self-hosted agent, running on the same laptop as Minikube) — applies all manifests in `k8s/` and waits for the rollout to complete.

A self-hosted agent is required for the Deploy stage because Microsoft-hosted cloud agents have no network path to a local-only Minikube cluster.

## Kubernetes setup

- **Cluster**: Minikube, single node, Docker driver
- **Deployment**: 2-5 replicas (managed by the HPA), pulling `eograk/sampleapp:latest` from Docker Hub
- **Service**: NodePort, exposing the app on port `30080`
- **Probes**: Liveness, Readiness, and Startup probes all check `GET /health`
- **Resources**: CPU/Memory requests and limits defined per pod, required for both the scheduler and the HPA
- **Autoscaling**: HorizontalPodAutoscaler scales between 2 and 5 replicas based on CPU (70%) and Memory (80%) utilization, verified working under real generated load

## Monitoring

Prometheus and Grafana are deployed in-cluster via the `kube-prometheus-stack` Helm chart, in a dedicated `monitoring` namespace. Access Grafana locally with:

```bash
export POD_NAME=$(kubectl --namespace monitoring get pod -l "app.kubernetes.io/name=grafana,app.kubernetes.io/instance=prometheus" -oname)
kubectl --namespace monitoring port-forward $POD_NAME 3000
```

Then open `http://localhost:3000` (default username `admin`; get the password with):

```bash
kubectl --namespace monitoring get secrets prometheus-grafana -o jsonpath="{.data.admin-password}" | base64 -d; echo
```

KPIs tracked: CPU Saturation, Memory Usage, Pod restart count (error rate proxy), and Deployment replica health. Latency/APM-level tracing is a known gap — it would require application-level instrumentation not currently implemented.

## Automated testing

`SampleApp.Tests` uses xUnit with `WebApplicationFactory<Program>` to run real integration tests against the in-memory app (not mocked). Five tests cover both endpoints, including verifying the Celsius-to-Fahrenheit calculation logic.

Tests run in three places:

1. **Locally**, on demand: `dotnet test SampleApp.Tests/SampleApp.Tests.csproj`
2. **Pre-merge**, automatically on every GitHub Pull Request (via `azure-pipelines-pr.yml`)
3. **Post-merge**, automatically as part of the main CD pipeline, blocking Docker build/push/deploy if any test fails

## Local development setup

Prerequisites: Docker, .NET 10 SDK, `kubectl`, `minikube`, `helm`.

```bash
# Start the cluster
minikube start --driver=docker

# Deploy everything
kubectl apply -f k8s/

# Get the app's URL
minikube service sampleapp-service --url

# Test it
curl <url>/health
curl <url>/weatherforecast
```

After a machine reboot, Docker, Minikube, and the self-hosted pipeline agent all need to be started manually before the pipeline's Deploy stage or local `kubectl` commands will work.

## Bonus features implemented

- Liveness, Readiness, and Startup probes
- Horizontal Pod Autoscaler, load-tested and verified (2 → 4 replicas under sustained CPU load, gradual scale-down afterward)
- Automated tests with pre-merge (GitHub PR) and post-merge (Azure CD) gating
- Multi-Region/Multi-Zone failover architecture (conceptual design)

## Known limitations

- Single-node Minikube cluster: no true node-level or region-level redundancy
- No application-level latency/APM metrics (infrastructure-level monitoring only)
- Requires the laptop and self-hosted agent to be running for automated deploys to trigger
