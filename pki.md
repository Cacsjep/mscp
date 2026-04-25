# PKI Admin Plugin
A complete certificate managment system that allow users in managment client
to import certificates, create certifciates and export.

# Features
Create Certificates (ROOT CA, Intermmediate CA, Client Certs) All comon props need to be able eg full x509 from commons props to extentsion and SANS (DNS and IP)
Export Certifcates (All common formats, pem, der, p12, pfx, crt) and also for private keys all commont formats
Import Certificates (Certificates must be able to be importet with all common formats, sittiged together ones like cert and key in pem must also be possilbe)
Nice overview of certifcates with things like expire dates nice datagrid with option to search filter or trigger the export, or delete etc.
We need to get sure we cant delete a root ca or ca when other cert has it as issuer.

# Question
Since we are bounded to windows and net 4.7 (MIP SDK) the question is how we safe store cert data?

# Layout nodes
We Should use nice node hirachy like 
PKI
- Certificates
-- CA Certificates
--- Root Certificates
--- Intermediate Certificat
-- Client Certifcates
--- HTTPS Certificates
--- 802.1X Certificates
--- Service Certificates