# Deploy the Workload

The cluster now has an [Traefik configured with a TLS certificate](./13-secret-managment-and-ingress-controller.md). The last step in the process is to deploy the workload, which will demonstrate the system's functions.

## Expected results

TODO

## Steps

1. Clone the source code for the workload

   TODO: If we do this right, we can have the build directly download and run the build from github, no need to clone.  Let's make sure we set it up to do that easily.

   ```bash
   cd ../..
   git clone -b feature/regulated-web-api https://github.com/mspnp/aks-secure-baseline aks-example-workload
   cd aks-example-workload/SimpleChainApi/
   ```

1. Use your Azure Container Registry build agents to build and quarantine the workload

   ```bash
   ACR_NAME_QUARANTINE=$(az deployment group show -g rg-bu0001a0005 -n cluster-stamp --query properties.outputs.containerRegistryName.value -o tsv)

   az acr build -f ./SimpleChainApi/Dockerfile -t quarantine/a0005/chain-api:1.0 -r $ACR_NAME_QUARANTINE -g rg-bu0001a0005 --platform linux/amd64 --target build . --agent-pool acragent

   # You may see BlobNotFound error messages in the early part of the build, this is
   # okay, and you shouldn't terminate the command. It's waiting for source code to
   # be uploaded.
   ```

   We are using your own dedicated image build agents here, in a dedicated subnet, for this process. Securing your workload pipeline components are critical to having a compliant solution. Ensure your build pipeline matches your desired security posture. Consider performing image building in an Azure Container Registry that is network-isolated from your clusters (unlike what we're showing here where it's on the same virtual network for simplicity.) Ensure build logs are captured (streamed at build time, or available afterwards via `az acr taskrun logs` or series of direct API calls). That build instance might also serve as your quarantine instance as well. Once the build is complete and post-build audits are complete, then it can be imported to your "live" registry.

1. Release the workload image from quarantine

   TODO: Consider returning a quarantine registry in the outputs of the arm template so these lines can technically look different, even if they are the same values :)

   ```bash
   # Get your Azure Container Registry service names (in this reference implementation, they are technically the same.)
   ACR_NAME_QUARANTINE=$(az deployment group show -g rg-bu0001a0005 -n cluster-stamp --query properties.outputs.containerRegistryName.value -o tsv)
   ACR_NAME=$(az deployment group show -g rg-bu0001a0005 -n cluster-stamp --query properties.outputs.containerRegistryName.value -o tsv)

   az acr import --source quarantine/a0005/chain-api:1.0 -r $ACR_NAME_QUARANTINE -t live/a0005/chain-api:1.0 -n $ACR_NAME
   ```










> :book: The Contoso app team is about to conclude this journey, but they need an app to test their new infrastructure. For this task they've picked out the venerable [ASP.NET Core Docker sample web app](https://github.com/dotnet/dotnet-docker/tree/master/samples/aspnetapp).

1. Deploy the ASP.NET Core Docker sample web app

   > The workload definition demonstrates the inclusion of a Pod Disruption Budget rule, ingress configuration, and pod (anti-)affinity rules for your reference.

   ```bash
   kubectl apply -f https://raw.githubusercontent.com/mspnp/aks-secure-baseline/main/workload/aspnetapp.yaml
   ```

1. Wait until is ready to process requests running

   ```bash
   kubectl wait --namespace a0008 --for=condition=ready pod --selector=app.kubernetes.io/name=aspnetapp --timeout=90s
   ```

1. Check your Ingress resource status as a way to confirm the AKS-managed Internal Load Balancer is functioning

   > In this moment your Ingress Controller (Traefik) is reading your ingress resource object configuration, updating its status, and creating a router to fulfill the new exposed workloads route. Please take a look at this and notice that the address is set with the Internal Load Balancer IP from the configured subnet.

   ```bash
   kubectl get ingress aspnetapp-ingress -n a0008
   ```

   > At this point, the route to the workload is established, SSL offloading configured, and a network policy is in place to only allow Traefik to connect to your workload. Therefore, please expect a `403` HTTP response if you attempt to connect to it directly.

1. Give a try and expect a `403` HTTP response

   ```bash
   kubectl -n a0008 run -i --rm --tty curl --image=mcr.microsoft.com/powershell --limits=cpu=200m,memory=128M -- curl -kI https://bu0001a0008-00.aks-ingress.contoso.com -w '%{remote_ip}\n'
   ```

## Security tooling

Your compliant cluster architecture requires a compliant inner loop development practice as well. Since this walkthrough is not focused on inner loop development practices, please dedicate some time to documenting your safe deployment practices and your workload's supply chain and hardening techniques. Consider using solutions like [GitHub Action's container-scan](https://github.com/Azure/container-scan) to check for container-level hardening concerns -- CIS benchmark alignment, CVE detections, etc. even before the image is pushed to your container registry.

### Next step

:arrow_forward: [End-to-End Validation](./15-validation.md)
