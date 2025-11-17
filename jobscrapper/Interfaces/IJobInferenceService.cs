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
        void TrainAnswerModels(List<JobSample> data);
        void SaveCategoryModel(string path);
        void SaveAnswerModels(string folder);
        void LoadCategoryModel(string path);
        void LoadAnswerModels(string folder);
        string PredictJobCategory(string question);
        string PredictAnswer(string question);

        void SaveCategoryAndAnswerModels(string folder);
        List<MatchResult> PredictMatches(ResumeInput resume, int topN);
    }
}
