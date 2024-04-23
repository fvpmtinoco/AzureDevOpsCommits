<h2># AzureDevOpsCommits</h2>
<p>This application leverages the Azure DevOps <a target="_blank" rel="noopener noreferrer" href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/?view=azure-devops-rest-7.1">API </a>to automate the retrieval of repository names and pull requests from a specified date, aimed at evaluating if the introduction of GitHub Copilot has affected the frequency and nature of code changes by developers. 
  It examines each pull request to extract commits and conducts comparisons to assess modifications in the code, using the <a target="_blank" rel="noopener noreferrer" href="https://github.com/mmanela/diffplex/tree/master/DiffPlex/DiffBuilder">DiffBuilder </a>library.</p>
<p>The application employs Serilog for comprehensive logging and is configured to forward these logs to Grafana Loki, which facilitates detailed monitoring and analysis through Grafana dashboards. This setup was requested from my current organization for understanding whether GitHub Copilot has led to more frequent code updates and changes by developers.</p>

#TODO
Consider setting up a local service infrastructure with Docker, incorporating Loki and Grafana images along with a non-relational database such as MongoDB. This setup would enable the storage of data previously obtained from DevOps APIs, reducing the frequency of API calls.
