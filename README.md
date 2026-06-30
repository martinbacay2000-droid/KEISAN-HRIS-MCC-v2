# KeisanHRIS_v2
KEISAN HRIS version 2

## Table of Contents

- [About the Project](#about-the-project)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Usage](#usage)
- [Bonus Tips and Hints](#Bonus-Tips-and-Hints)
- [Technologies Used](#technologies-used)
- [Contact](#contact)

---

## About the Project

Keisan HRIS is a comprehensive Human Resources Information System (HRIS) designed to streamline and automate essential HR and payroll management tasks. The system serves as a centralized platform for managing employee data, simplifying payroll processing, and enhancing overall organizational efficiency.

Key features of Keisan HRIS typically include:

*Employee Data Management: A secure database for storing and managing all employee information, including personal details, employment history, and contact information.

*Payroll Processing: An integrated system for accurately calculating employee wages, taxes, and deductions, ensuring timely and compliant payroll cycles.

*HR Automation: Tools that automate routine HR workflows, such as leave management, time and attendance tracking, and reporting.

*Secure & Centralized: A single, unified platform that reduces manual data entry, minimizes errors, and provides a secure location for sensitive employee information.

*Report Generation: Generate reports such as employee's data (employee's information,timekeeping), payroll data

## Prerequisites


* VISUAL STUDIO 2022 ( https://www.mediafire.com/file/2zf1hjrsedlozpq/VisualStudio2022_Enter_Setup.zip/file )
* MYSQL 8.0
* GIT BASH 
* Database Management and development tool (HEIDI SQL, NAVICAT, MYSQL WORK BENCH)

## Getting Started

Setup Git Bash and GitHub
1. Install Git Bash 
First, download and install Git Bash from the official website. You'll be presented with several options during installation; for most users, the default settings are fine. This will install Git on your system and provide the Git Bash terminal for executing Git commands.

2. Configure Git Bash 
Open Git Bash. You need to configure your identity so that Git can correctly attribute your commits. Enter the following commands, replacing the placeholder text with your information:

  * git config --global user.name "Your Name"
  * git config --global user.email "your.email@example.com"
  * The --global flag ensures these settings apply to all your Git repositories on your computer.


Get and Set Up a Branch
1. Clone the Repository 
Navigate to the directory where you want to store your project. Use the git clone command with your repository's URL. This copies the entire repository, including all its branches, to your local machine.

* git clone https://github.com/johnadri12/KeisanHRIS_v2.git

2. Navigate to the Repository Directory 
After cloning, a new directory named KeisanHRIS_v2 will be created. Change into this directory using the cd command.


* cd KeisanHRIS_v2

3. Check Out the Specific Branch 
To work on a specific branch, such as adrian_branch, you need to switch to it. Use the git checkout command.

* git checkout adrian_branch

4. Pull Latest Changes (Optional but Recommended) 
It's a good practice to pull the latest changes from the remote branch to ensure your local branch is up to date before you start working.

* git pull origin adrian_branch

Commit and Push Your Changes
1. Make Changes 
Now you can start making changes to the files in your local KeisanHRIS_v2 directory.

2. Stage Your Changes 
After making changes, use git add to stage the files you want to include in your commit. To stage all modified files, use:

* git add .

3. Commit Your Changes 
Commit your staged changes with a descriptive message. This creates a snapshot of your changes in your local repository.

* git commit -m "Briefly describe your changes here"

4. Push Your Changes to GitHub 
Finally, use git push to upload your local commits to the remote branch on GitHub.


* git push origin adrian_branch

If this is your first time pushing to this branch, you may need to set the upstream branch:

* git push --set-upstream origin adrian_branch
* Future pushes will be simpler, just git push.

## Bonus Tips and Hints

IF you want to update your branch with the latest changes from main.
That’s called merging main into your branch (or sometimes rebasing).

Here’s how you do it:

🔹 Option 1: Merge main into your branch (safe & simple)

Make sure you’re on your branch:

* git checkout your-branch


Fetch the latest from GitHub:

* git fetch origin


Merge main into your branch:

* git merge origin/main


If there are conflicts, fix them → then:

* git add .
* git commit


Push updated branch:

* git push origin your-branch

🔹 Option 2: Rebase (cleaner history)

Instead of merging, you can rebase:

* git checkout your-branch
* git fetch origin
* git rebase origin/main


Fix any conflicts → git add . → git rebase --continue

Finally push (may need force if history changed):

git push origin your-branch --force


⚡ Example if your branch is called feature/profile-update:

* git checkout feature/profile-update
* git fetch origin
* git merge origin/main
* git push origin feature/profile-update

## Technologies Used

* mysql 8.0
* Asp.net MVC Core
* Fillow Admin Template 
* plugins that came along with the Fillow Admin Template (jquery, boostrap and etc....)

## Contact
- Email: jcapili@northlogic.com.ph
- Messenger: https://m.me/johnadrian.capili

