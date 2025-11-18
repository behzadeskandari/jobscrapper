using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jobscrapper.Models;

namespace jobscrapper.Interfaces
{
    public interface IJobInferenceService
    {
        List<JobSample> LoadAllJobSamples(string path);
        void TrainCategoryModel(List<JobSample> data);
        void TrainPerCategoryModels(List<JobSample> data);  // New: Trains model_*.zip per category
        void SaveCategoryModel(string path);
        void SavePerCategoryModels(string folder);  // Saves model_*.zip
        void SaveCategoryAndPerCategoryModels(string folder);  // Combined save
        void LoadCategoryModel(string path);
        void LoadPerCategoryModels(string folder);  // Loads model_*.zip
        string PredictJobCategory(string question);
        string PredictAnswer(string question);
        List<MatchResult> PredictMatches(ResumeInput resume, int topN);

        void TrainAnswerModels(List<JobSample> data);
        void SaveAnswerModels(string folder);
        void LoadAnswerModels(string folder);

        void SaveCategoryAndAnswerModels(string folder);







    }
}
