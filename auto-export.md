### New Plugin: Auto Exporter
Type: Admin Plugin + Event Server 

## High level description
Its not possible to have auto exports in Milestone, therefore the new auto exporter plugin
should solve this issue.

## Featureset
- Jobs: We want to have mutliple Jobs
- Global Config:
  - Filepath, standart file paths but also network eg mounted network shares i think its unc paths or so.
  - Its a rind storage:
    - MAX GB: we delete the oldes to make palce in case folder eg destination is full
    - MAX TIME: we delete the oldes written files when its to old.
- Exection List:
  - A view that shows exectued jobs, if there are errors or if it was successfull the timerange and the produced GB data.
  - I want also to manual invoke here a job to test if it works correctly
  - It would be awesome if we can show also a progress here so i see after a manual infoke ok now its on 10% or so


# Job
A job represent a configuration for an export job.

## Job Configuration
- Name
- Format: Like Milestone we should give user the posiblity to set xprotext export or avi and encryption.
- Days to export like (Last 1 Day, Last 2 Day, Last 4 Days etc... up to Last Month) here it would be cool to make a select field with values based on the second select that show minutes, hours, days, month.
- Individual Cameras, or a camera group
- Job Execution:
    We dont trigger it by our self, we use action manager, and therefore use can create a rule an decide when the job is exectued.


# File Strucure
- Storage
  - Job
    - %date like 28.05.2026
      - camera name
        - EXPORTET DATA

The execution of jobs happen in event server background plugin, we configure via MIP messageing our background part.
Examples and code for reference 
- G:\mscp\mscp\Admin Plugins\Auditor
- G:\mscp\mscp\Admin Plugins\CertWatchdog
- G:\mscp\mscp\Admin Plugins\MetadataViewer
- https://github.com/milestonesys/mipsdk-samples-component/tree/main/ExportSample <- Importend one

Ask me question if someting unlcear of about implementation or oder ides that comes into your mind !!!!